namespace RealArtists.ShipHub.Common.GitHub.Models {
  public enum GitHubAccountType {
    Organization,
    User,
  }

  public class Account {
    public long Id { get; set; }
    public string Login { get; set; }
    public GitHubAccountType Type { get; set; }
  }
}
