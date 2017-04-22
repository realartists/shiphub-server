namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Text;
  using Newtonsoft.Json;
  using RealArtists.ShipHub.Common.Hashing;

  public class ReviewTableType {
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Body { get; set; }
    public string CommitId { get; set; }
    public string State { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    // Computed from the review properties.
    [JsonIgnore]
    public Guid Hash {
      get {
        using (var hash = new MurmurHash3()) {
          var hashBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(this.SerializeObject()));
          return new Guid(hashBytes);
        }
      }
    }
  }
}
