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
  using se = Messages.Entries;

  public class SyncContext {
    private ShipHubPrincipal _user;
    private ISyncConnection _connection;
    private SyncVersions _versions;
    private DateTimeOffset? _lastRecordedUsage;

    private VersionDetails VersionDetails {
      get {
        return new VersionDetails() {
          Organizations = _versions.OrgVersions.Select(x => new OrganizationVersion() { Id = x.Key, Version = x.Value }),
          Repositories = _versions.RepoVersions.Select(x => new RepositoryVersion() { Id = x.Key, Version = x.Value }),
        };
      }
    }

    public SyncContext(ShipHubPrincipal user, ISyncConnection connection, SyncVersions initialVersions) {
      _user = user;
      _connection = connection;
      _versions = initialVersions;
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
      using (var context = new ShipHubContext()) {
        await SendSyncResponse(context);
        await SendSubscriptionEntry(context);
        await RecordUsage(context);
      }
    }

    private async Task SendSubscriptionEntry(ShipHubContext context) {
      SubscriptionResponse response;
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
        } else {
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

      await _connection.SendJsonAsync(response);
    }

    private async Task RecordUsage(ShipHubContext context) {
      DateTimeOffset utcNow = DateTimeOffset.UtcNow;

      // We only have to record usage once per calendar day.
      if (_lastRecordedUsage == null || _lastRecordedUsage?.DayOfYear != utcNow.DayOfYear) {
        await context.RecordUsage(_user.UserId, utcNow);
        _lastRecordedUsage = utcNow;
      }
    }

    public async Task SendSyncResponse(ShipHubContext context) {
      var pageSize = 1000;
      var tasks = new List<Task>();

      var dsp = context.PrepareSync(
        _user.Token,
        pageSize,
        _versions.RepoVersions.Select(x => new VersionTableType() {
          ItemId = x.Key,
          RowVersion = x.Value,
        }),
        _versions.OrgVersions.Select(x => new VersionTableType() {
          ItemId = x.Key,
          RowVersion = x.Value,
        })
      );

      var entries = new List<SyncLogEntry>();
      var sentLogs = 0;
      using (var reader = await dsp.ExecuteReaderAsync()) {
        dynamic ddr = reader;

        /* ************************************************************************************************************
         * Basic User Info
         * ***********************************************************************************************************/

        long? userId = null;
        Common.GitHub.GitHubRateLimit rateLimit = null;
        if (reader.Read()) {
          userId = (long)ddr.UserId;
          rateLimit = new Common.GitHub.GitHubRateLimit() {
            RateLimit = ddr.RateLimit,
            RateLimitRemaining = ddr.RateLimitRemaining,
            RateLimitReset = ddr.RateLimitReset,
          };
        }

        if (userId == null || userId != _user.UserId) {
          await _connection.CloseAsync();
          return;
        }

        if (rateLimit.IsUnder(Common.GitHub.GitHubHandler.RateLimitFloor)) {
          tasks.Add(_connection.SendJsonAsync(new RateLimitResponse() {
            Until = rateLimit.RateLimitReset
          }));
        }

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
                },
              });
            }
          }

          // Comments (can be deleted)
          reader.NextResult();
          while (reader.Read()) {
            var entry = new SyncLogEntry() {
              Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
              Entity = SyncEntityType.Comment,
            };

            if (entry.Action == SyncLogAction.Set) {
              entry.Data = new CommentEntry() {
                Body = ddr.Body,
                CreatedAt = ddr.CreatedAt,
                Identifier = ddr.Id,
                Issue = ddr.IssueId,
                Repository = ddr.RepositoryId,
                UpdatedAt = ddr.UpdatedAt,
                User = ddr.UserId,
              };
            } else {
              entry.Data = new CommentEntry() { Identifier = ddr.Id };
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

          // Issues
          reader.NextResult();
          while (reader.Read()) {
            entries.Add(new SyncLogEntry() {
              Action = SyncLogAction.Set,
              Entity = SyncEntityType.Issue,
              Data = new IssueEntry() {
                Assignees = issueAssignees.Val((long)ddr.Id, () => new List<long>()),
                Body = ddr.Body,
                ClosedAt = ddr.ClosedAt,
                ClosedBy = ddr.ClosedById,
                CreatedAt = ddr.CreatedAt,
                Identifier = ddr.Id,
                Labels = issueLabels.Val((long)ddr.Id, () => new List<long>()),
                Locked = ddr.Locked,
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
              },
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
                Disabled = ddr.Disabled,
              },
            });
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
          }));
        }
      }

      await Task.WhenAll(tasks);
    }
  }
}
