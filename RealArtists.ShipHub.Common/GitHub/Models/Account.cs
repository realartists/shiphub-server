namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System.Diagnostics.CodeAnalysis;

  public enum GitHubAccountType {
    Organization,
    User,
  }

  public class Account {
    public long Id { get; set; }
    public string Login { get; set; }

    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    public GitHubAccountType Type { get; set; }
  }
}
