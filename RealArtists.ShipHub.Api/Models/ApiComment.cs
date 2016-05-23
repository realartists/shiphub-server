namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiComment {
    public long Identifier { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long UserId { get; set; }
  }
}