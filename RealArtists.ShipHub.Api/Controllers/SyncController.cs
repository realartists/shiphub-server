namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
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
        var handler = new SyncConnection();
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

    public SyncConnection()
      : base(_MaxMessageSize) {
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

    private async Task SyncIt(HelloMessage hello) {
      var syncResponse = new SyncMessage();
      var entries = new List<SyncLogEntry>();

      using (var context = new ShipHubContext()) {
        // Get all known changes
        var logsByType = await context.RepositoryLogs
          .GroupBy(x => x.Type)
          .ToArrayAsync();

        foreach (var group in logsByType) {
          switch (group.Key) {
            case "account":
              var accountIds = group.Select(x => x.ItemId).ToHashSet();
              var accounts = await context.Accounts.Where(x => accountIds.Contains(x.Id)).ToArrayAsync();
              entries.AddRange(accounts.Select(x => new SyncLogEntry() {
                Action = SyncLogAction.Set, // TODO: Handle deletion
                Entity = typeof(User).IsAssignableFrom(x.GetType()) ? SyncEntityType.User : SyncEntityType.Organization,
                Data = new AccountEntry() {
                  Identifier = x.Id,
                  Login = x.Login,
                },
              }));
              break;
            case "comment":
              var commentIds = group.Select(x => x.ItemId).ToHashSet();
              var comments = await context.Comments.Where(x => commentIds.Contains(x.Id)).ToArrayAsync();
              entries.AddRange(comments.Select(x => new SyncLogEntry() {
                Action = SyncLogAction.Set, // TODO: Handle deletion
                Entity = SyncEntityType.Comment,
                Data = new CommentEntry() {
                  Body = x.Body,
                  CreatedAt = x.CreatedAt,
                  Identifier = x.Id,
                  Issue = x.IssueId,
                  Reactions = x.Reactions.DeserializeObject<Reactions>(),
                  Repository = x.RepositoryId,
                  UpdatedAt = x.UpdatedAt,
                  User = x.UserId,
                },
              }));
              break;
            case "event":
              var eventIds = group.Select(x => x.ItemId).ToHashSet();
              var events = await context.IssueEvents.Where(x => eventIds.Contains(x.Id)).ToArrayAsync();
              entries.AddRange(events.Select(x => new SyncLogEntry() {
                Action = SyncLogAction.Set, // TODO: Handle deletion
                Entity = SyncEntityType.Event,
                Data = new EventEntry() {
                  Actor = x.ActorId,
                  Assignee = x.AssigneeId,
                  CommitId = x.CommitId,
                  CreatedAt = x.CreatedAt,
                  Event = x.Event,
                  ExtensionData = x.ExtensionData,
                  Identifier = x.Id,
                  Milestone = x.MilestoneId,
                  Repository = x.RepositoryId,
                },
              }));
              break;
            case "issue":
              var issueIds = group.Select(x => x.ItemId).ToHashSet();
              var issues = await context.Issues
                .Include(x => x.Labels)
                .Where(x => issueIds.Contains(x.Id)).ToArrayAsync();
              entries.AddRange(issues.Select(x => new SyncLogEntry() {
                Action = SyncLogAction.Set, // TODO: Handle deletion
                Entity = SyncEntityType.Issue,
                Data = new IssueEntry() {
                  Assignee = x.AssigneeId,
                  Body = x.Body,
                  ClosedAt = x.ClosedAt,
                  ClosedBy = x.ClosedById,
                  CreatedAt = x.CreatedAt,
                  Identifier = x.Id,
                  Labels = x.Labels.Select(y => new se.Label() {
                    Color = y.Color,
                    Name = y.Name,
                  }),
                  Locked = x.Locked,
                  Milestone = x.MilestoneId,
                  Number = x.Number,
                  Reactions = x.Reactions.DeserializeObject<Reactions>(),
                  Repository = x.RepositoryId,
                  State = x.State,
                  Title = x.Title,
                  UpdatedAt = x.UpdatedAt,
                  User = x.UserId,
                },
              }));
              break;
            case "milestone":
              var milestoneIds = group.Select(x => x.ItemId).ToHashSet();
              var milestones = await context.Milestones.Where(x => milestoneIds.Contains(x.Id)).ToArrayAsync();
              entries.AddRange(milestones.Select(x => new SyncLogEntry() {
                Action = SyncLogAction.Set, // TODO: Handle deletion
                Entity = SyncEntityType.Milestone,
                Data = new MilestoneEntry() {
                  ClosedAt = x.ClosedAt,
                  CreatedAt = x.CreatedAt,
                  Description = x.Description,
                  DueOn = x.DueOn,
                  Identifier = x.Id,
                  Number = x.Number,
                  Repository = x.RepositoryId,
                  State = x.State,
                  Title = x.Title,
                  UpdatedAt = x.UpdatedAt,
                },
              }));
              break;
            case "repository":
              var repoIds = group.Select(x => x.ItemId).ToHashSet();
              var repos = await context.Repositories
                .Include(x => x.Labels)
                .Include(x => x.AssignableAccounts)
                .Where(x => repoIds.Contains(x.Id)).ToArrayAsync();
              entries.AddRange(repos.Select(x => new SyncLogEntry() {
                Action = SyncLogAction.Set, // TODO: Handle deletion
                Entity = SyncEntityType.Repository,
                Data = new RepositoryEntry() {
                  Assignees = x.AssignableAccounts.Select(y => y.Id).ToArray(),
                  Owner = x.AccountId,
                  FullName = x.FullName,
                  Identifier = x.Id,
                  Labels = x.Labels.Select(y => new se.Label() {
                    Color = y.Color,
                    Name = y.Name,
                  }),
                  Name = x.Name,
                  Private = x.Private,
                },
              }));
              break;
            default:
              // Ignore for now
              break;
          }
        }

        // TODO: Version?
      }

      syncResponse.Logs = entries;
      syncResponse.Remaining = 0;

      await SendJsonAsync(syncResponse);
    }
  }
}
