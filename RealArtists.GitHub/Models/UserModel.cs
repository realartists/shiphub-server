namespace RealArtists.GitHub.Models {
  using System;

  public class UserModel {
    public int Id { get; set; }
    public string AvatarUrl { get; set; }
    public string Company { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
