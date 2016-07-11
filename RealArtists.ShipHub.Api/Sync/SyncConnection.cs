namespace RealArtists.ShipHub.Api.Sync {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.IO.Compression;
  using System.Linq;
  using System.Net.WebSockets;
  using System.Reactive.Concurrency;
  using System.Reactive.Linq;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.WebSockets;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.WebSockets;
  using Messages;
  using Messages.Entries;
  using Newtonsoft.Json.Linq;
  using se = Messages.Entries;

  public class SyncConnection : WebSocketHandler {
    private const int _MaxMessageSize = 64 * 1024; // 64 KB

    private SyncManager _syncManager;
    private IDisposable _subscription;

    public long UserId { get; private set; }
    public SyncVersions SyncVersions { get; private set; } = new SyncVersions();

    private VersionDetails VersionDetails {
      get {
        return new VersionDetails() {
          Organizations = SyncVersions.OrgVersions.Select(x => new OrganizationVersion() { Id = x.Key, Version = x.Value }),
          Repositories = SyncVersions.RepoVersions.Select(x => new RepositoryVersion() { Id = x.Key, Version = x.Value }),
        };
      }
    }

    public SyncConnection(long userId, SyncManager syncManager)
      : base(_MaxMessageSize) {
      UserId = userId;
      _syncManager = syncManager;
    }

    public override Task OnClose() {
      UnsubscribeFromChanges();
      return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public override Task OnMessage(byte[] message) {
      var gzip = message[0] == 1;
      string json = "";
      if (gzip) {
        using (var ms = new MemoryStream(message))
        using (var df = new GZipStream(ms, CompressionMode.Decompress))
        using (var tr = new StreamReader(df, Encoding.UTF8)) {
          ms.ReadByte(); // eat gzip flag
          json = tr.ReadToEnd();
        }
      } else {
        json = Encoding.UTF8.GetString(message, 1, message.Length - 1);
      }

      return OnMessage(json);
    }

    public override async Task OnMessage(string message) {
      var jobj = JObject.Parse(message);
      var data = jobj.ToObject<SyncRequestBase>(JsonUtility.SaneSerializer);
      switch (data.MessageType) {
        case "hello":
          // parse message, update local versions
          var hello = jobj.ToObject<HelloRequest>(JsonUtility.SaneSerializer);
          if (hello.Versions != null) {
            if (hello.Versions.Organizations != null) {
              SyncVersions.OrgVersions = hello.Versions.Organizations.ToDictionary(x => x.Id, x => x.Version);
            }
            if (hello.Versions.Repositories != null) {
              SyncVersions.RepoVersions = hello.Versions.Repositories.ToDictionary(x => x.Id, x => x.Version);
            }
          }

          // Do initial sync
          await Sync();

          // Subscribe
          SubscribeToChanges();
          return;
        default:
          // Ignore unknown messages for now
          return;
      }
    }

    public Task AcceptWebSocketRequest(AspNetWebSocketContext context) {
      return ProcessWebSocketRequestAsync(context.WebSocket, CancellationToken.None);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public Task SendJsonAsync(object o) {
      using (var ms = new MemoryStream()) {
        ms.WriteByte(1);

        using (var df = new GZipStream(ms, CompressionLevel.Optimal))
        using (var sw = new StreamWriter(df, Encoding.UTF8)) {
          JsonUtility.SaneSerializer.Serialize(sw, o);
          sw.Flush();
        }

        return SendAsync(new ArraySegment<byte>(ms.ToArray()), WebSocketMessageType.Binary, true);
      }
    }

    private void SubscribeToChanges() {
      if (_subscription != null) {
        throw new InvalidOperationException("Already subscribed to changes.");
      }

      // Subscribe to changes
      _syncManager.Changes
        .ObserveOn(TaskPoolScheduler.Default)
        .Select(changes => Observable.FromAsync(async () => {
          // Check if this user is affected and if so, sync.
          if (changes.Organizations.Intersect(SyncVersions.OrgVersions.Keys).Any()
            || changes.Repositories.Intersect(SyncVersions.RepoVersions.Keys).Any()) {
            await Sync();
          }
        }))
        .Concat()
        .Subscribe(
          changes => { /* Work already done */ },
          error => {
            // TODO: Logging?
            // TODO: Disconnect? Resubscribe?
#if DEBUG
            Debugger.Break();
#endif
          },
          () => { /* This should never occur. */ });
    }

    private void UnsubscribeFromChanges() {
      // Unsubscribe from changes
      if (_subscription != null) {
        _subscription.Dispose();
        _subscription = null;
      }
    }

    private async Task Sync() {
      var pageSize = 1000;
      var tasks = new List<Task>();

      using (var context = new ShipHubContext()) {
        var dsp = context.PrepareWhatsNew(
          UserId,
          pageSize,
          SyncVersions.RepoVersions.Select(x => new VersionTableType() {
            ItemId = x.Key,
            RowVersion = x.Value,
          }),
          SyncVersions.OrgVersions.Select(x => new VersionTableType() {
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
            SyncVersions.RepoVersions.Remove(repoId);
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
            SyncVersions.OrgVersions.Remove(orgId);
          }

          // Send
          if (entries.Any()) {
            tasks.Add(SendJsonAsync(new SyncResponse() {
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
                  SyncVersions.RepoVersions[(long)ddr.RepositoryId] = (long)ddr.RowVersion;
                }

                // Send page
                if (entries.Any()) {
                  sentLogs += entries.Count();
                  tasks.Add(SendJsonAsync(new SyncResponse() {
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
                  SyncVersions.OrgVersions[(long)ddr.OrganizationId] = (long)ddr.RowVersion;
                }

                // Send orgs
                if (entries.Any()) {
                  tasks.Add(SendJsonAsync(new SyncResponse() {
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
