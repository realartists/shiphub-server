namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json.Linq;

  public class IssueEntry : SyncEntity {
    public long Identifier { get; set; }
    public long User { get; set; }
    public long Repository { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public long? Milestone { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public bool PullRequest { get; set; }
    public ReactionSummary ShipReactionSummary { get; set; }

    public long? PullRequestIdentifier { get; set; }
    public DateTimeOffset? PullRequestUpdatedAt { get; set; }
    public bool? MaintainerCanModify { get; set; }
    public bool? Mergeable { get; set; }
    public string MergeCommitSha { get; set; }
    public bool? Merged { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public long? MergedBy { get; set; }

    // Gross
    public JToken Base { get; set; }
    public JToken Head { get; set; }

    public IEnumerable<long> Assignees { get; set; }
    public IEnumerable<long> Labels { get; set; }
  }
}
