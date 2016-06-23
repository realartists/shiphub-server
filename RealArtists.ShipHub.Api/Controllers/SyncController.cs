namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.IO.Compression;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Net.WebSockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web;
  using System.Web.Http;
  using System.Web.WebSockets;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.WebSockets;
  using Newtonsoft.Json.Linq;
  using SyncMessages;
  using SyncMessages.Entries;
  using se = SyncMessages.Entries;

  [RoutePrefix("api/sync")]
  public class SyncController : ShipHubController {
    [Route("")]
    [HttpGet]
    public HttpResponseMessage Sync() {
      var context = HttpContext.Current;
      if (context.IsWebSocketRequest) {
        var handler = new SyncConnection(ShipUser.UserId);
        context.AcceptWebSocketRequest(handler.AcceptWebSocketRequest, new AspNetWebSocketOptions() { SubProtocol = "V1" });
        return new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
      }

      var reason = "WebSocket connection required.";
      return new HttpResponseMessage(HttpStatusCode.UpgradeRequired) {
        ReasonPhrase = reason,
        Content = new StringContent(reason, Encoding.UTF8, MediaTypeNames.Text.Plain),
      };
    }
  }

  public class SyncConnection : WebSocketHandler {
    private const int _MaxMessageSize = 64 * 1024; // 64 KB

    private long _userId;

    public SyncConnection(long userId)
      : base(_MaxMessageSize) {
      _userId = userId;
    }

    public override Task OnClose() {
      // TODO: Remove from active connections
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

    public override Task OnMessage(string message) {
      var jobj = JObject.Parse(message);
      var data = jobj.ToObject<SyncMessageBase>(JsonUtility.SaneSerializer);
      switch (data.MessageType) {
        case "hello":
          return SyncIt(jobj.ToObject<HelloMessage>(JsonUtility.SaneSerializer));
        default:
          // Ignore unknown messages for now
          return Task.CompletedTask;
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

    private static readonly RepositoryVersion[] _EmptyRepoVersion = new RepositoryVersion[0];
    private static readonly OrganizationVersion[] _EmptyOrgVersion = new OrganizationVersion[0];

    private async Task SyncIt(HelloMessage hello) {
      var pageSize = 100;
      var tasks = new List<Task>();

      using (var context = new ShipHubContext()) {
        var clientRepoVersions = hello.Versions?.Repositories ?? _EmptyRepoVersion;
        var clientOrgVersions = hello.Versions?.Organizations ?? _EmptyOrgVersion;
        var dsp = context.PrepareWhatsNew(
          _userId,
          pageSize,
          clientRepoVersions.Select(x => new VersionTableType() {
            ItemId = x.Id,
            RowVersion = x.Version,
          }),
          clientOrgVersions.Select(x => new VersionTableType() {
            ItemId = x.Id,
            RowVersion = x.Version,
          })
        );

        var entries = new List<SyncLogEntry>();
        var sentLogs = 0;
        var repoVersions = clientRepoVersions.ToDictionary(x => x.Id, x => x.Version);
        var orgVersions = clientOrgVersions.ToDictionary(x => x.Id, x => x.Version);
        using (var reader = await dsp.ExecuteReaderAsync()) {
          dynamic ddr = reader;

          // Total logs
          reader.Read();
          var totalLogs = (long)ddr.TotalLogs;

          // TODO: Deleted / revoked repos?

          while (reader.NextResult()) {
            // Get type
            reader.Read();
            int type = ddr.Type;

            switch (type) {
              case 1:
                entries.Clear();

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
                  issueLabels
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
                  repoVersions[(long)ddr.RepositoryId] = (long)ddr.RowVersion;
                }

                // Send page
                sentLogs += entries.Count();
                tasks.Add(SendJsonAsync(new SyncMessage() {
                  Logs = entries,
                  Remaining = totalLogs - sentLogs,
                  Version = new VersionDetails() {
                    Organizations = orgVersions.Select(x => new OrganizationVersion() { Id = x.Key, Version = x.Value }),
                    Repositories = repoVersions.Select(x => new RepositoryVersion() { Id = x.Key, Version = x.Value }),
                  }
                }));
                break;
              case 2:
                // TODO: Orgs
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
