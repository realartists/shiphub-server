using System.Collections.Generic;

namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class WebhookIssuePayload {
    public string Action { get; set; }
    public Issue Issue { get; set; }
    public Comment Comment { get; set; }
    public Milestone Milestone { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
  }

  public class WebhookPushPayload {
    public string Ref { get; set; }
    public long Size { get; set; } // The number of commits in the push
    public long DistinctSize { get; set; }
    public Repository Repository { get; set; }
    public IEnumerable<WebhookPushCommit> Commits { get; set; }
  }

  public class WebhookPushCommit {
    public IEnumerable<string> Added { get; set; }
    public IEnumerable<string> Removed { get; set; }
    public IEnumerable<string> Modified { get; set; }
  }
}