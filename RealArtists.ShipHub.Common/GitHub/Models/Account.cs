namespace RealArtists.ShipHub.Common.GitHub.Models {
  public enum GitHubAccountType {
    Organization,
    User,
  }

  public class Account {
    public int Id { get; set; }
    public string AvatarUrl { get; set; }
    public string Login { get; set; }
    public GitHubAccountType Type { get; set; }
  }
}
