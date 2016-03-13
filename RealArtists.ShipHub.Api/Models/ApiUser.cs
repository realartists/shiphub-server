namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiUser {
    public int Identifier { get; set; }
    public string AvatarUrl { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
  }
}