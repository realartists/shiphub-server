namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Text;
  using Newtonsoft.Json;
  using RealArtists.ShipHub.Common.Hashing;

  public class PullRequestTableType {
    // Required
    public long Id { get; set; }
    public int Number { get; set; }
    public long? IssueId { get; set; }
    
    // In list and full response
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string MergeCommitSha { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public string BaseJson { get; set; }
    public string HeadJson { get; set; }
    
    // Only in full response
    public int? Additions { get; set; }
    public int? ChangedFiles { get; set; }
    public int? Commits { get; set; }
    public int? Deletions { get; set; }
    public bool? MaintainerCanModify { get; set; }
    public bool? Mergeable { get; set; }
    public string MergeableState { get; set; }
    public long? MergedById { get; set; }
    public bool? Rebaseable { get; set; }

    // Change tracking(only set for full response)
    [JsonIgnore]
    public Guid? Hash {
      get {
        // Only bother if this is a "complete" entry.
        // This will break if they add Mergeable to the list response.
        // TODO: Find a better way to indicate this
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
