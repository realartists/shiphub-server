namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiEvent {
    public string CommitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Event { get; set; }
    public int Identifier { get; set; }
  }
}