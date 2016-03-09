namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiUser {
    public Guid Identifier { get; set; }
    public int GitHubId { get; set; }
    public string AvatarUrl { get; set; }
    public string Company { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
  }
}