namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public enum GitHubAccountType {
    Organization,
    User,
  }

  public class Account  {
    public int Id { get; set; }
    public string AvatarUrl { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public GitHubAccountType Type { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
