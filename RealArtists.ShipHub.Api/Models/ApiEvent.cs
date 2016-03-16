namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiEvent {
    public int Identifier { get; set; }
    public string CommitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Event { get; set; }
  }
}