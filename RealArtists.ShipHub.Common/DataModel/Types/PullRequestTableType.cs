namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class PullRequestTableType : IssueTableType {
    public long PullRequestId { get; set; }
    public bool MaintainerCanModify { get; set; }
    public bool Mergeable { get; set; }
    public string MergeCommitSha { get; set; }
    public bool Merged { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public long? MergedById { get; set; }
    public string BaseJson { get; set; }
    public string HeadJson { get; set; }
  }
}
