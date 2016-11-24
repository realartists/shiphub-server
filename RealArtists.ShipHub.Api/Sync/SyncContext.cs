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

    public bool ShouldSync(ChangeSummary changes) {
      // Check if this user is affected and if so, sync.
      return changes.Organizations.Overlaps(_versions.OrgVersions.Keys)
        || changes.Repositories.Overlaps(_versions.RepoVersions.Keys)
        || changes.Users.Contains(_user.UserId);
    }

    private async Task<SubscriptionResponse> GetSubscriptionEntry() {
      using (var context = new ShipHubContext()) {
        var personalSub = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == _user.UserId);
        var orgs = await context.OrganizationAccounts
          .Include(x => x.Organization.Subscription)
          .Where(x => x.UserId == _user.UserId)
          .Select(x => x.Organization)
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
          mode = SubscriptionMode.Trial;
          trialEndDate = personalSub.TrialEndDate;
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

        return new SubscriptionResponse() {
          Mode = mode,
          TrialEndDate = trialEndDate,
          ManageSubscriptionsRefreshHash = hashString,
        };
      }
    }

    private async Task RecordUsage() {
      DateTimeOffset utcNow = DateTimeOffset.UtcNow;

      // We only have to record usage once per calendar day.
      if (_lastRecordedUsage == null || _lastRecordedUsage?.DayOfYear != utcNow.DayOfYear) {
        using (var context = new ShipHubContext()) {
          await context.RecordUsage(_user.UserId, utcNow);
        }
        _lastRecordedUsage = utcNow;
      }
    }

    public async Task Sync() {
      var pageSize = 1000;
      var tasks = new List<Task>();

      using (var context = new ShipHubContext()) {
        var dsp = context.PrepareWhatsNew(
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

          // Check if token still valid.
          long? userId = null;
          if (reader.Read()) {
            userId = (long?)ddr.UserId;
          }

          if (userId == null || userId != _user.UserId) {
            await _connection.CloseAsync();
            return;
          }

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

          // Send
          if (entries.Any()) {
            tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
              Logs = entries,
              Remaining = 0,
              Versions = VersionDetails,
            }));
          }

          // Total logs
          reader.NextResult();
          reader.Read();
          var totalLogs = (long)ddr.TotalLogs;

          // TODO: Deleted / revoked repos?

          while (reader.NextResult()) {
            // Get type
            reader.Read();
            int type = ddr.Type;

            entries.Clear();

            switch (type) {
              case 1:
                // Accounts
                reader.NextResult();
                while (reader.Read()) {
                  entries.Add(new SyncLogEntry() {
                    Action = SyncLogAction.Set,
                    Entity = (string)ddr.Type == "user" ? SyncEntityType.User : SyncEntityType.Organization,
                    Data = new AccountEntry() {
                      Identifier = ddr.Id,
                      Login = ddr.Login,
                    },
                  });
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
                      IssueTemplate = ddr.IssueTemplate
                    },
                  });
                }

                // Versions
                reader.NextResult();
                while (reader.Read()) {
                  _versions.RepoVersions[(long)ddr.RepositoryId] = (long)ddr.RowVersion;
                }

                // Send page
                sentLogs += entries.Count();
                tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
                  Logs = entries,
                  Remaining = totalLogs - sentLogs,
                  Versions = VersionDetails,
                }));
                break;
              case 2:
                var orgAccounts = new Dictionary<long, OrganizationEntry>();
                var orgMembers = new Dictionary<long, List<long>>();

                // Accounts
                reader.NextResult();
                while (reader.Read()) {
                  if ((string)ddr.Type == "user") {
                    entries.Add(new SyncLogEntry() {
                      Action = SyncLogAction.Set,
                      Entity = SyncEntityType.User,
                      Data = new AccountEntry() {
                        Identifier = ddr.Id,
                        Login = ddr.Login,
                      }
                    });
                  } else {
                    var org = new OrganizationEntry() {
                      Identifier = ddr.Id,
                      Login = ddr.Login,
                      ShipNeedsWebhookHelp = !(ddr.HasHook || ddr.Admin),
                      // Users set later
                    };
                    orgAccounts.Add(org.Identifier, org);
                  }
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

                // Versions
                reader.NextResult();
                while (reader.Read()) {
                  _versions.OrgVersions[(long)ddr.OrganizationId] = (long)ddr.RowVersion;
                }

                // Send orgs
                if (entries.Any()) {
                  tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
                    Logs = entries,
                    Remaining = 0, // Orgs are last
                    Versions = VersionDetails,
                  }));
                }
                break;
              default:
                throw new InvalidOperationException($"Invalid page type: {type}");
            }
          }
        }
      }

      var subscriptionEntry = await GetSubscriptionEntry();
      tasks.Add(_connection.SendJsonAsync(subscriptionEntry));
      tasks.Add(RecordUsage());

      await Task.WhenAll(tasks);
    }
  }
}
