namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class Review {
    public long Id { get; set; }
    public Account User { get; set; }
    public string Body { get; set; }
    public string CommitId { get; set; }
    public string State { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
  }
}
