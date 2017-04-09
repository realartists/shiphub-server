namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Text;
  using RealArtists.ShipHub.Common.Hashing;

  public class PullRequestTableType : IssueTableType {
    public long PullRequestId { get; set; }
    public bool? MaintainerCanModify { get; set; }
    public bool? Mergeable { get; set; }
    public string MergeCommitSha { get; set; }
    public bool? Merged { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public long? MergedById { get; set; }
    public string BaseJson { get; set; }
    public string HeadJson { get; set; }

    public Guid? Hash {
      get {
        // Only bother if this is a "complete" entry.
        if (Mergeable == null) {
          return null;
        }

        using (var hashFunction = new MurmurHash3()) {
          var hash = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(this.SerializeObject()));
          return new Guid(hash);
        }
      }
    }
  }
}
