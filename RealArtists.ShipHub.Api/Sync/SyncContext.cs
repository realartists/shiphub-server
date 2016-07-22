namespace RealArtists.ShipHub.Api.Sync {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Filters;
  using Messages;
  using Messages.Entries;
  using se = Messages.Entries;

  public class SyncContext {
    private ShipHubPrincipal _user;
    private SyncConnection _connection;
    private SyncVersions _versions;

    private VersionDetails VersionDetails {
      get {
        return new VersionDetails() {
          Organizations = _versions.OrgVersions.Select(x => new OrganizationVersion() { Id = x.Key, Version = x.Value }),
          Repositories = _versions.RepoVersions.Select(x => new RepositoryVersion() { Id = x.Key, Version = x.Value }),
        };
      }
    }

    public SyncContext(ShipHubPrincipal user, SyncConnection connection, SyncVersions initialVersions) {
      _user = user;
      _connection = connection;
      _versions = initialVersions;
    }

    public bool ShouldSync(ChangeSummary changes) {
      // Check if this user is affected and if so, sync.
      return changes.Organizations.Intersect(_versions.OrgVersions.Keys).Any()
        || changes.Repositories.Intersect(_versions.RepoVersions.Keys).Any();
    }

    public async Task Sync() {
      var pageSize = 1000;
      var tasks = new List<Task>();

      using (var context = new ShipHubContext()) {
        var dsp = context.PrepareWhatsNew(
          _user.UserId,
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

          // Removed Repos
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
            entries.Add(new SyncLogEntry() {
              Action = SyncLogAction.Delete,
              Entity = SyncEntityType.Organization,
              Data = new OrganizationEntry() {
                Identifier = orgId,
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

                // Comments
                reader.NextResult();
                while (reader.Read()) {
                  entries.Add(new SyncLogEntry() {
                    Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                    Entity = SyncEntityType.Comment,
                    Data = new CommentEntry() {
                      Body = ddr.Body,
                      CreatedAt = ddr.CreatedAt,
                      Identifier = ddr.Id,
                      Issue = ddr.IssueId,
                      Reactions = ((string)ddr.Reactions).DeserializeObject<Reactions>(),
                      Repository = ddr.RepositoryId,
                      UpdatedAt = ddr.UpdatedAt,
                      User = ddr.UserId,
                    },
                  });
                }

                // Events
                reader.NextResult();
                while (reader.Read()) {
                  entries.Add(new SyncLogEntry() {
                    Action = SyncLogAction.Set,
                    Entity = SyncEntityType.Event,
                    Data = new IssueEventEntry() {
                      Actor = ddr.ActorId,
                      Assignee = ddr.AssigneeId,
                      CommitId = ddr.CommitId,
                      CreatedAt = ddr.CreatedAt,
                      Event = ddr.Event,
                      ExtensionData = ddr.ExtensionData,
                      Identifier = ddr.Id,
                      Issue = ddr.IssueId,
                      Repository = ddr.RepositoryId,
                    },
                  });
                }

                // Milestones
                reader.NextResult();
                while (reader.Read()) {
                  entries.Add(new SyncLogEntry() {
                    Action = (bool)ddr.Delete ? SyncLogAction.Delete : SyncLogAction.Set,
                    Entity = SyncEntityType.Milestone,
                    Data = new MilestoneEntry() {
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
                    },
                  });
                }

                // Issue Labels
                var issueLabels = new Dictionary<long, List<se.Label>>();
                reader.NextResult();
                while (reader.Read()) {
                  issueLabels
                    .Valn((long)ddr.IssueId)
                    .Add(new se.Label() {
                      Color = ddr.Color,
                      Name = ddr.Name,
                    });
                }

                // Issues
                reader.NextResult();
                while (reader.Read()) {
                  entries.Add(new SyncLogEntry() {
                    Action = SyncLogAction.Set,
                    Entity = SyncEntityType.Issue,
                    Data = new IssueEntry() {
                      Assignee = ddr.AssigneeId,
                      Body = ddr.Body,
                      ClosedAt = ddr.ClosedAt,
                      ClosedBy = ddr.ClosedById,
                      CreatedAt = ddr.CreatedAt,
                      Identifier = ddr.Id,
                      Labels = issueLabels.Val((long)ddr.Id),
                      Locked = ddr.Locked,
                      Milestone = ddr.MilestoneId,
                      Number = ddr.Number,
                      Reactions = ((string)ddr.Reactions).DeserializeObject<Reactions>(),
                      Repository = ddr.RepositoryId,
                      State = ddr.State,
                      Title = ddr.Title,
                      UpdatedAt = ddr.UpdatedAt,
                      User = ddr.UserId,
                    },
                  });
                }

                // Repository Labels
                var repoLabels = new Dictionary<long, List<se.Label>>();
                reader.NextResult();
                while (reader.Read()) {
                  repoLabels
                    .Valn((long)ddr.RepositoryId)
                    .Add(new se.Label() {
                      Color = ddr.Color,
                      Name = ddr.Name,
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
                      Assignees = repoAssignable.Val((long)ddr.Id),
                      Owner = ddr.AccountId,
                      FullName = ddr.FullName,
                      Identifier = ddr.Id,
                      Labels = repoLabels.Val((long)ddr.Id),
                      Name = ddr.Name,
                      Private = ddr.Private,
                    },
                  });
                }

                // Versions
                reader.NextResult();
                while (reader.Read()) {
                  _versions.RepoVersions[(long)ddr.RepositoryId] = (long)ddr.RowVersion;
                }

                // Send page
                if (entries.Any()) {
                  sentLogs += entries.Count();
                  tasks.Add(_connection.SendJsonAsync(new SyncResponse() {
                    Logs = entries,
                    Remaining = totalLogs - sentLogs,
                    Versions = VersionDetails,
                  }));
                }
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

      await Task.WhenAll(tasks);
    }
  }
}
