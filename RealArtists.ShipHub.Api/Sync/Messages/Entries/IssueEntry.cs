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

    // PR only fields
    public long? PullRequestIdentifier { get; set; }
    public string MergeCommitSha { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public JToken Base { get; set; }
    public JToken Head { get; set; }

    public int? Additions { get; set; }
    public int? ChangedFiles { get; set; }
    public int? Commits { get; set; }
    public int? Deletions { get; set; }
    public bool? MaintainerCanModify { get; set; }
    public bool? Mergeable { get; set; }
    public string MergeableState { get; set; }
    public long? MergedBy { get; set; }
    public bool? Rebaseable { get; set; }

    // Backward compatibility
    public bool Merged => MergedBy != null;

    public IEnumerable<long> Assignees { get; set; }
    public IEnumerable<long> Labels { get; set; }
    public IEnumerable<long> RequestedReviewers { get; set; }
  }
}
