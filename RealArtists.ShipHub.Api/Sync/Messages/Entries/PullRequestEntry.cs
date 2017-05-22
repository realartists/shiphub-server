namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json.Linq;

  public class PullRequestEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Issue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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

    public IEnumerable<long> RequestedReviewers { get; set; }
  }
}
