namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System.Diagnostics.CodeAnalysis;

  public enum GitHubAccountType {
    // This catches errors where the type is not changed from the default value.
    Unspecified = 0,
    Organization,
    User,
    Bot, // WTF? undocumented
  }

  public class Account {
    public long Id { get; set; }
    public string Login { get; set; }

    // Only set if user has chosen to show their name on their GitHub profile.
    public string Name { get; set; }
    public string Company { get; set; }
    public string Email { get; set; }

    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    public GitHubAccountType Type { get; set; }
  }
}
