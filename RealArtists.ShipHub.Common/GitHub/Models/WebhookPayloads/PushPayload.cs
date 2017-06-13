namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;

  public class PushPayload {
    public string Ref { get; set; }
    public string Before { get; set; }
    public string After { get; set; }
    public bool Created { get; set; }
    public bool Deleted { get; set; }
    public bool Forced { get; set; }
    public string BaseRef { get; set; }
    public IEnumerable<PushCommit> Commits { get; set; }
    public PushCommit HeadCommit { get; set; }
    public Repository Repository { get; set; }
    public PushPusher Pusher { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }

    // The docs at https://developer.github.com/v3/activity/events/types/#pushevent
    // say these exist, but I've not ever seen them in a real hook event.
    public long Size { get; set; } // The number of commits in the push
    public long DistinctSize { get; set; }
  }

  public class PushPusher {
    public string Name { get; set; }
    public string Email { get; set; }
  }

  public class PushCommit {
    public string Id { get; set; }
    public string TreeId { get; set; }
    public bool Distinct { get; set; }
    public string Message { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public PushCommitUser Author { get; set; }
    public PushCommitUser Committer { get; set; }
    public IEnumerable<string> Added { get; set; }
    public IEnumerable<string> Removed { get; set; }
    public IEnumerable<string> Modified { get; set; }
  }

  public class PushCommitUser {
    public string Name { get; set; }
    public string Email { get; set; }

    [JsonProperty("username")]
    public string UserName { get; set; }
  }
}
