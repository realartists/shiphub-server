namespace RealArtists.ShipHub.Api.Sync {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.Hashing;
  using Filters;
  using Messages;
  using Messages.Entries;
  using Newtonsoft.Json.Linq;
  using RealArtists.ShipHub.Legacy;

  public class SyncContext {
    private static readonly Version MinimumSelectiveSyncVersion = new Version(662, 0);

    private ShipHubPrincipal _user;
    private ISyncConnection _connection;
    private SyncVersions _versions;
    private DateTimeOffset? _lastRecordedUsage;
    private bool _selectiveSyncEnabled;

    private VersionDetails VersionDetails => new VersionDetails() {
      Organizations = _versions.OrgVersions.Select(x => new OrganizationVersion() { Id = x.Key, Version = x.Value }),
      Repositories = _versions.RepoVersions.Select(x => new RepositoryVersion() { Id = x.Key, Version = x.Value }),
      PullRequestVersion = _versions.PullRequestVersion,
    };

    public SyncContext(ShipHubPrincipal user, ISyncConnection connection, SyncVersions initialVersions) {
      _user = user;
      _connection = connection;
      _versions = initialVersions;
      _selectiveSyncEnabled = connection.ClientBuild >= MinimumSelectiveSyncVersion;

      RunUpgradeCheck();
    }

    private static readonly Version MinimumPullRequestClientVersion = new Version(580, 0);
    private const long MinimumPullRequestVersion = 1;
    private void RunUpgradeCheck() {
      // _connection.ClientBuild cannot be null, hello will force upgrade
      if (_connection.ClientBuild >= MinimumPullRequestClientVersion
        && _versions.PullRequestVersion < MinimumPullRequestVersion) {
        _versions = new SyncVersions(
          _versions.RepoVersions.ToDictionary(x => x.Key, x => 0L),
          _versions.OrgVersions.ToDictionary(x => x.Key, x => 0L),
          MinimumPullRequestVersion
        );
      }
    }

    public async Task SendHelloResponse(Guid purgeIdentifier) {
      using (var context = new ShipHubContext()) {
        var spiderProgress = await SpiderProgress(context);

        await _connection.SendJsonAsync(
          new HelloResponse() {
            PurgeIdentifier = purgeIdentifier,
            SpiderProgress = spiderProgress
          }
        );
      }
    }

    private bool ShouldSync(ChangeSummary changes) {
      // Check if this user is affected and if so, sync.
      return changes.Organizations.Overlaps(_versions.OrgVersions.Keys)
        || changes.Repositories.Overlaps(_versions.RepoVersions.Keys)
        || changes.Users.Contains(_user.UserId);
    }

    public async Task Sync(ChangeSummary changes) {
      if (!ShouldSync(changes)) {
        Log.Debug(() => $"User {_user.UserId} not syncing for changes {changes}");
        return;
      }
      Log.Debug(() => $"User {_user.UserId} is syncing for changes {changes}");
      await SendSubscriptionEntry();
      await SendSyncResponse();
      await RecordUsage();
    }

    private async Task SendSubscriptionEntry() {
      SubscriptionResponse response;
      using (var context = new ShipHubContext()) {
        var personalSub = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == _user.UserId);
        var orgs = await context.OrganizationAccounts
          .Where(x => x.UserId == _user.UserId)
          .Select(x => x.Organization)
          .Include(x => x.Subscription)
          .ToListAsync();
        var numOfSubscribedOrgs = orgs
          .Where(x => x.Subscription != null && x.Subscription.StateName.Equals(SubscriptionState.Subscribed.ToString()))
          .Count();

        SubscriptionMode mode;
        DateTimeOffset? trialEndDate = null;

        if (personalSub == null) {
          mode = SubscriptionMode.Paid;
        } else if (numOfSubscribedOrgs > 0) {
          mode = SubscriptionMode.Paid;
        } else if (personalSub.State == SubscriptionState.Subscribed) {
          mode = SubscriptionMode.Paid;
        } else if (personalSub.State == SubscriptionState.InTrial) {
          if (personalSub.TrialEndDate > DateTimeOffset.UtcNow) {
            mode = SubscriptionMode.Trial;
            trialEndDate = personalSub.TrialEndDate;
          } else { // personalSub.TrialEndDate is null or in the past
            mode = SubscriptionMode.Free;
          }
        } else {
          mode = SubscriptionMode.Free;
        }

        var userState = $"{_user.UserId}:{personalSub?.StateName}";
        var orgStates = orgs
          .OrderBy(x => x.Id)
          .Select(x => $"{x.Id}:{x.Subscription?.StateName}");

        string hashString;
        using (var hashFunction = new MurmurHash3()) {
          var str = string.Join(";", new[] { userState }.Concat(orgStates));
          var hash = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(str));
          hashString = new Guid(hash).ToString();
        }

        response = new SubscriptionResponse() {
          Mode = mode,
          TrialEndDate = trialEndDate,
          ManageSubscriptionsRefreshHash = hashString,
        };
      }

      await _connection.SendJsonAsync(response);
    }

    private async Task RecordUsage() {
      var utcNow = DateTimeOffset.UtcNow;

      // We only have to record usage once per calendar day.
      if (_lastRecordedUsage == null || _lastRecordedUsage?.DayOfYear != utcNow.DayOfYear) {
        using (var context = new ShipHubContext()) {
          await context.RecordUsage(_user.UserId, utcNow);
        }
        _lastRecordedUsage = utcNow;
      }
    }

    private SyncSpiderProgress ReadSpiderProgress(DynamicDataReader reader) {
      dynamic ddr = reader;

      SyncSpiderProgress spiderProgress = null;

      if (!reader.Read()) {
        return null;
      }
      var hasRepoList = ddr.HasRepoMetadata;

      if (!reader.NextResult()) {
        return null;
      }

      var repoSpiderProgress = new List<(bool SyncStarted, int MaxIssueNumber, int IssueCount)>();
      while (reader.Read()) {
        bool hasIssueMetadata = ddr.HasIssueMetadata;
        bool issuesFullyImported = ddr.IssuesFullyImported;
        int maxNumber = ddr.MaxNumber ?? 0;
        int issueCount = ddr.IssueCount ?? 0;

        if (issuesFullyImported) {
          // realartists/shiphub-server#460 Handle spider progress for repos with gaps in issue numbering
          // workaround gaps in issues numbers that can occur in some rare scenarios
          maxNumber = issueCount;
        }

        repoSpiderProgress.Add((hasIssueMetadata || maxNumber != 0, maxNumber, issueCount));
      }

      var hasReposThatHaveNeverFetchedIssues = repoSpiderProgress.Exists(t => !t.SyncStarted);

      if (!hasRepoList || hasReposThatHaveNeverFetchedIssues) {
        spiderProgress = new SyncSpiderProgress() {
          Summary = "Fetching Repo List",
          Progress = -1.0
        };
      } else {
        var (expected, loaded) = repoSpiderProgress.Aggregate((expected: 0, loaded: 0), (accum, t) => {
          return (expected: t.MaxIssueNumber + accum.expected, loaded: t.IssueCount + accum.loaded);
        });
        if (loaded >= expected) {
          spiderProgress = new SyncSpiderProgress() {
            Summary = "Issues Fully Fetched",
            Progress = 1.0
          };
        } else {
          spiderProgress = new SyncSpiderProgress() {
            Summary = "Fetching Issues",
            Progress = loaded / (double)expected
          };
        }
      }

      return spiderProgress;
    }

    private async Task<SyncSpiderProgress> SpiderProgress(ShipHubContext context) {
      var dsp = context.SyncSpiderProgress(_user.UserId, _selectiveSyncEnabled);
      using (var reader = await dsp.ExecuteReaderAsync()) {
        return ReadSpiderProgress(reader);
      }
    }

    public async Task SendSyncResponse() {
      var pageSize = 1000;
      var tasks = new List<Task>();

      using (var context = new ShipHubContext()) {
        var dsp = context.PrepareSync(
          _user.UserId,
          pageSize,
          _versions.RepoVersions.Select(x => new VersionTableType() {
            ItemId = x.Key,
            RowVersion = x.Value,
          }),
          _versions.OrgVersions.Select(x => new VersionTableType() {
            ItemId = x.Key,
            RowVersion = x.Value,
          }),
          _selectiveSyncEnabled
        );

        var entries = new List<SyncLogEntry>();
        var sentLogs = 0;
        SyncSpiderProgress spiderProgress = null;

        using (var reader = await dsp.ExecuteReaderAsync()) {
          dynamic ddr = reader;

          /* ************************************************************************************************************
           * Basic User Info
           * ***********************************************************************************************************/

          long? userId = null;
          Common.GitHub.GitHubRateLimit rateLimit = null;
          if (reader.Read()) {
            userId = (long)ddr.UserId;
            rateLimit = new Common.GitHub.GitHubRateLimit(
              null,
              ddr.RateLimit,
              ddr.RateLimitRemaining,
              ddr.RateLimitReset);
          }

          if (userId == null || userId != _user.UserId) {
            await _connection.CloseAsync();
            return;
          }

          if (rateLimit?.IsExceeded == true) {
            // HACK: to quiet modal dialogs in some clients
            var limitUntil = rateLimit.Reset;
            var now = DateTimeOffset.UtcNow;
            var thirtyMinutes = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
            thirtyMinutes = thirtyMinutes.AddMinutes(((now.Minute / 30) + 1) * 30);
            if (limitUntil < thirtyMinutes) {
              limitUntil = thirtyMinutes;
            }

            tasks.Add(_connection.SendJsonAsync(new RateLimitResponse() {
              Until = limitUntil,
            }));
          }

          /* ************************************************************************************************************
           * Spider Progress
           * ************************************************************************************************************/

          reader.NextResult();
          spiderProgress = ReadSpiderProgress(reader);

          /* ************************************************************************************************************
           * Deleted orgs and repos (permission removed or deleted)
           * ***********************************************************************************************************/

          // Removed Repos
          reader.NextResult();
          while (reader.Read()) {
            long repoId = ddr.RepositoryId;
            entries.Add(new SyncLogEntry() {
              Action = SyncLogAction.Delete,
              Entity = SyncEntityType.Repository,
              Data = new RepositoryEntry() {
                Identifier = repoId,
              },
            });
            _versions.RepoVersions.Remove(repoId);
          }

          // Removed Orgs
          reader.NextResult();
          while (reader.Read()) {
            long orgId = ddr.OrganizationId;
            // Don't delete the org
            entries.Add(new SyncLogEntry() {
              Action = SyncLogAction.Set,
              Entity = SyncEntityType.Organization,
              Data = new OrganizationEntry() {
                Identifier = orgId,
                Login = ddr.Login ?? "Sync Corruption: 0x2695",
                Users = Array.Empty<long>()
              },
            });
            _versions.OrgVersions.Remove(orgId);
          }

          if (entries.Any()) {
            tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
              Logs = entries,
              Remaining = 0,
              Versions = VersionDetails,
              SpiderProgress = spiderProgress
            }));
            entries = new List<SyncLogEntry>();
          }

          /* ************************************************************************************************************
           * New/Updated Orgs
           * ***********************************************************************************************************/

          var orgAccounts = new Dictionary<long, OrganizationEntry>();
          var orgMembers = new Dictionary<long, List<long>>();

          // Orgs
          reader.NextResult();
          while (reader.Read()) {
            var org = new OrganizationEntry() {
              Identifier = ddr.Id,
              Login = ddr.Login,
              Name = ddr.Name,
              ShipNeedsWebhookHelp = !(ddr.HasHook || ddr.Admin),
              // Users set later
            };
            orgAccounts.Add(org.Identifier, org);
          }

          // Org Membership
          reader.NextResult();
          while (reader.Read()) {
            orgMembers.Valn((long)ddr.OrganizationId).Add((long)ddr.UserId);
          }

          // Fixup
          foreach (var kv in orgMembers) {
            orgAccounts[kv.Key].Users = kv.Value;
          }
          entries.AddRange(orgAccounts.Values.Select(x => new SyncLogEntry() {
            Action = SyncLogAction.Set,
            Entity = SyncEntityType.Organization,
            Data = x,
          }));

          // Can't update versions just yet. Have to make sure we send the account entities first.

          if (entries.Any()) {
            tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
              Logs = entries,
              Remaining = 0, // Orgs are sent as a single batch
              Versions = VersionDetails,
              SpiderProgress = spiderProgress
            }));
            entries = new List<SyncLogEntry>();
          }

          /* ************************************************************************************************************
           * New/Updated entites (paginated)
           * ***********************************************************************************************************/

          // Total logs
          reader.NextResult();
          reader.Read();
          var totalLogs = (long)ddr.TotalEntries;

          // Pagination Loop
          while (reader.NextResult()) {
            entries = new List<SyncLogEntry>();

            // Accounts
            // Orgs sent here will have null members, which the client ignores.
            while (reader.Read()) {
              var type = (string)ddr.Type == "user" ? SyncEntityType.User : SyncEntityType.Organization;
              var accountId = (long)ddr.Id;
              if (type == SyncEntityType.Organization && orgAccounts.ContainsKey(accountId)) {
                // We've already sent that information
                --totalLogs;
              } else {
                entries.Add(new SyncLogEntry() {
                  Action = SyncLogAction.Set,
                  Entity = type,
                  Data = new AccountEntry() {
                    Identifier = accountId,
                    Login = ddr.Login,
                    Name = ddr.Name,
                  },
                });
              }
            }

            // Commit Comments
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.CommitComment,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new CommitCommentEntry() {
                  Body = ddr.Body,
                  CommitId = ddr.CommitId,
                  CreatedAt = ddr.CreatedAt,
                  Identifier = ddr.Id,
                  Line = ddr.Line,
                  Path = ddr.Path,
                  Position = ddr.Position,
                  Repository = ddr.RepositoryId,
                  UpdatedAt = ddr.UpdatedAt,
                  User = ddr.UserId,
                };
              } else {
                entry.Data = new CommitCommentEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Issue Comments
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.IssueComment,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new IssueCommentEntry() {
                  Body = ddr.Body,
                  CreatedAt = ddr.CreatedAt,
                  Identifier = ddr.Id,
                  Issue = ddr.IssueId,
                  Repository = ddr.RepositoryId,
                  UpdatedAt = ddr.UpdatedAt,
                  User = ddr.UserId,
                };
              } else {
                entry.Data = new IssueCommentEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Events
            reader.NextResult();
            while (reader.Read()) {
              string eventType = ddr.Event;

              var data = new IssueEventEntry() {
                Actor = ddr.ActorId,
                CreatedAt = ddr.CreatedAt,
                Event = eventType,
                ExtensionData = ddr.ExtensionData,
                Identifier = ddr.Id,
                Issue = ddr.IssueId,
                Repository = ddr.RepositoryId,
              };

              var eventEntry = new SyncLogEntry() {
                Action = SyncLogAction.Set,
                Entity = SyncEntityType.Event,
                Data = data,
              };

              if (ddr.Restricted) {
                // closed event is special
                if (eventType == "closed") {
                  // strip all extra info
                  // See https://realartists.slack.com/archives/general/p1470075341001004
                  data.ExtensionDataDictionary.Clear();
                } else {
                  // Account for missing logs in progress reports
                  --totalLogs;
                  continue;
                }
              }

              entries.Add(eventEntry);
            }

            // Milestones (can be deleted)
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.Milestone,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new MilestoneEntry() {
                  ClosedAt = ddr.ClosedAt,
                  CreatedAt = ddr.CreatedAt,
                  Description = ddr.Description,
                  DueOn = ddr.DueOn,
                  Identifier = ddr.Id,
                  Number = ddr.Number,
                  Repository = ddr.RepositoryId,
                  State = ddr.State,
                  Title = ddr.Title,
                  UpdatedAt = ddr.UpdatedAt,
                };
              } else {
                entry.Data = new MilestoneEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Projects (can be deleted)
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.Project,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new ProjectEntry() {
                  Identifier = ddr.Id,
                  Name = ddr.Name,
                  Number = ddr.Number,
                  Body = ddr.Body,
                  CreatedAt = ddr.CreatedAt,
                  UpdatedAt = ddr.UpdatedAt,
                  Creator = ddr.CreatorId,
                  Organization = ddr.OrganizationId,
                  Repository = ddr.RepositoryId
                };
              } else {
                entry.Data = new ProjectEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Reactions (can be deleted)
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.Reaction,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new ReactionEntry() {
                  Comment = ddr.CommentId,
                  Content = ddr.Content,
                  CreatedAt = ddr.CreatedAt,
                  Identifier = ddr.Id,
                  Issue = ddr.IssueId,
                  PullRequestComment = ddr.PullRequestCommentId,
                  CommitComment = ddr.CommitCommentId,
                  User = ddr.UserId,
                };
              } else {
                entry.Data = new ReactionEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Labels
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.Label,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new LabelEntry() {
                  Color = ddr.Color,
                  Identifier = ddr.Id,
                  Name = ddr.Name,
                  Repository = ddr.RepositoryId,
                };
              } else {
                entry.Data = new LabelEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Issue Labels
            var issueLabels = new Dictionary<long, List<long>>();
            reader.NextResult();
            while (reader.Read()) {
              issueLabels
                .Valn((long)ddr.IssueId)
                  .Add((long)ddr.LabelId);
            }

            // Issue Assignees
            var issueAssignees = new Dictionary<long, List<long>>();
            reader.NextResult();
            while (reader.Read()) {
              issueAssignees
                .Valn((long)ddr.IssueId)
                .Add((long)ddr.UserId);
            }

            // Pull Request Reviewers
            var prReviewers = new Dictionary<long, List<long>>();
            reader.NextResult();
            while (reader.Read()) {
              prReviewers
                .Valn((long)ddr.IssueId)
                .Add((long)ddr.UserId);
            }

            // Issue Mentions
            var issueMentions = new Dictionary<long, List<long>>();
            reader.NextResult();
            while (reader.Read()) {
              issueMentions
                .Valn((long)ddr.IssueId)
                .Add((long)ddr.UserId);
            }

            // Issues (can be deleted)
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.Issue,
              };

              if (entry.Action == SyncLogAction.Set) {
                var issueEntry = new IssueEntry() {
                  Assignees = issueAssignees.Val((long)ddr.Id, () => new List<long>()),
                  Body = ddr.Body,
                  ClosedAt = ddr.ClosedAt,
                  ClosedBy = ddr.ClosedById,
                  CreatedAt = ddr.CreatedAt,
                  Identifier = ddr.Id,
                  Labels = issueLabels.Val((long)ddr.Id, () => new List<long>()),
                  Locked = ddr.Locked,
                  Mentions = issueMentions.Val((long)ddr.Id, () => new List<long>()),
                  Milestone = ddr.MilestoneId,
                  Number = ddr.Number,
                  // This is hack that works until GitHub changes their version
                  ShipReactionSummary = ((string)ddr.Reactions).DeserializeObject<ReactionSummary>(),
                  Repository = ddr.RepositoryId,
                  State = ddr.State,
                  Title = ddr.Title,
                  UpdatedAt = ddr.UpdatedAt,
                  PullRequest = ddr.PullRequest,
                  User = ddr.UserId,
                };
                entry.Data = issueEntry;
              } else {
                entry.Data = new IssueEntry() {
                  Identifier = ddr.Id
                };
              }
              
              entries.Add(entry);
            }

            // Pull Requests
            reader.NextResult();
            while (reader.Read()) {
              var prEntry = new PullRequestEntry() {
                Identifier = ddr.Id,
                Issue = ddr.IssueId,
                CreatedAt = ddr.CreatedAt,
                UpdatedAt = ddr.UpdatedAt,
                MergeCommitSha = ddr.MergeCommitSha,
                MergedAt = ddr.MergedAt,
                Base = ((string)ddr.BaseJson).DeserializeObject<JToken>(),
                Head = ((string)ddr.HeadJson).DeserializeObject<JToken>(),

                Additions = ddr.Additions,
                ChangedFiles = ddr.ChangedFiles,
                Commits = ddr.Commits,
                Deletions = ddr.Deletions,
                MaintainerCanModify = ddr.MaintainerCanModify,
                Mergeable = ddr.Mergeable,
                MergeableState = ddr.MergeableState,
                MergedBy = ddr.MergedById,
                Rebaseable = ddr.Rebaseable,

                RequestedReviewers = prReviewers.Val((long)ddr.IssueId, () => new List<long>()),
              };

              entries.Add(new SyncLogEntry() {
                Action = SyncLogAction.Set,
                Entity = SyncEntityType.PullRequest,
                Data = prEntry,
              });
            }

            // Repository Assignable Users
            var repoAssignable = new Dictionary<long, List<long>>();
            reader.NextResult();
            while (reader.Read()) {
              repoAssignable
                .Valn((long)ddr.RepositoryId)
                .Add((long)ddr.AccountId);
            }

            // Repositories
            reader.NextResult();
            while (reader.Read()) {
              entries.Add(new SyncLogEntry() {
                Action = SyncLogAction.Set,
                Entity = SyncEntityType.Repository,
                Data = new RepositoryEntry() {
                  Assignees = repoAssignable.Val((long)ddr.Id, () => new List<long>()),
                  Owner = ddr.AccountId,
                  FullName = ddr.FullName,
                  Identifier = ddr.Id,
                  Name = ddr.Name,
                  Private = ddr.Private,
                  ShipNeedsWebhookHelp = !(ddr.HasHook || ddr.Admin),
                  IssueTemplate = ddr.IssueTemplate,
                  PullRequestTemplate = ddr.PullRequestTemplate,
                  HasIssues = ddr.HasIssues,
                  Disabled = ddr.Disabled,
                },
              });
            }

            // Pull Request Reviews
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.Review,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new ReviewEntry() {
                  Body = ddr.Body,
                  CommitId = ddr.CommitId,
                  Identifier = ddr.Id,
                  Issue = ddr.IssueId,
                  State = ddr.State,
                  SubmittedAt = ddr.SubmittedAt,
                  User = ddr.UserId,
                };
              } else {
                entry.Data = new ReviewEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Pull Request Comments
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.PullRequestComment,
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new PullRequestCommentEntry() {
                  Body = ddr.Body,
                  CommitId = ddr.CommitId,
                  CreatedAt = ddr.CreatedAt,
                  DiffHunk = ddr.DiffHunk,
                  Identifier = ddr.Id,
                  Issue = ddr.IssueId,
                  OriginalCommitId = ddr.OriginalCommitId,
                  OriginalPosition = ddr.OriginalPosition,
                  Path = ddr.Path,
                  Position = ddr.Position,
                  Repository = ddr.RepositoryId,
                  Review = ddr.PullRequestReviewId,
                  UpdatedAt = ddr.UpdatedAt,
                  User = ddr.UserId,
                };
              } else {
                entry.Data = new PullRequestCommentEntry() { Identifier = ddr.Id };
              }

              entries.Add(entry);
            }

            // Commit Statuses
            reader.NextResult();
            while (reader.Read()) {
              entries.Add(new SyncLogEntry() {
                Action = SyncLogAction.Set,
                Entity = SyncEntityType.CommitStatus,
                Data = new CommitStatusEntry() {
                  Context = ddr.Context,
                  CreatedAt = ddr.CreatedAt,
                  Description = ddr.Description,
                  Identifier = ddr.Id,
                  Reference = ddr.Reference,
                  Repository = ddr.RepositoryId,
                  State = ddr.State,
                  TargetUrl = ddr.TargetUrl,
                  UpdatedAt = ddr.UpdatedAt,
                },
              });
            }

            // Protected Branches
            reader.NextResult();
            while (reader.Read()) {
              var entry = new SyncLogEntry() {
                Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                Entity = SyncEntityType.ProtectedBranch
              };

              if (entry.Action == SyncLogAction.Set) {
                entry.Data = new ProtectedBranchEntry() {
                  Repository = ddr.RepositoryId,
                  Identifier = ddr.Id,
                  Name = ddr.Name,
                  ExtensionData = ddr.Protection
                };
              } else {
                entry.Data = new ProtectedBranchEntry() {
                  Identifier = ddr.Id
                };
              }

              entries.Add(entry);
            }

            // Versions
            reader.NextResult();
            while (reader.Read()) {
              switch ((string)ddr.OwnerType) {
                case "org":
                  _versions.OrgVersions[(long)ddr.OwnerId] = (long)ddr.RowVersion;
                  break;
                case "repo":
                  _versions.RepoVersions[(long)ddr.OwnerId] = (long)ddr.RowVersion;
                  break;
                default:
                  throw new Exception($"Unknown OwnerType {ddr.OwnerType}");
              }
            }

            // Send page
            sentLogs += entries.Count();
            tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
              Logs = entries,
              Remaining = totalLogs - sentLogs,
              Versions = VersionDetails,
              SpiderProgress = spiderProgress
            }));
          }
        }

        await Task.WhenAll(tasks);
      }
    }
  }
}
