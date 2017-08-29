namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;

  public class RepositoryEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Owner { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public string IssueTemplate { get; set; }
    public string PullRequestTemplate { get; set; }
    public bool HasIssues { get; set; }
    public bool Disabled { get; set; }

    public IEnumerable<long> Assignees { get; set; } = Array.Empty<long>();
    public bool ShipNeedsWebhookHelp { get; set; }
  }
}
