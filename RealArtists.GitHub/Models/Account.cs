namespace RealArtists.GitHub.Models {
  public class Account : GitHubModel {
    public enum GitHubAccountType {
      Organization,
      User,
    }

    public string AvatarUrl { get; set; }
    public int Id { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public GitHubAccountType Type { get; set; }
  }
}
