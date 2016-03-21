namespace RealArtists.ShipHub.Api.GitHub.Models {
  public enum GitHubAccountType {
    Organization,
    User,
  }

  public class Account : GitHubModel {
    public int Id { get; set; }
    public string AvatarUrl { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public GitHubAccountType Type { get; set; }
  }
}
