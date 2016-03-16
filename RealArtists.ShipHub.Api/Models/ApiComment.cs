namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiComment {
    public int Identifier { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UserId { get; set; }
  }
}