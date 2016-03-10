namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class Account : IGitHubResource, IVersionedResource {
    public string TopicName { get { return Login; } }

    /// <summary>
    /// The account's GitHub unique ID.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [Required]
    [StringLength(4)]
    public string Type { get; set; }

    /// <summary>
    /// URL of the account's avatar.
    /// </summary>
    [StringLength(500)]
    public string AvatarUrl { get; set; }

    /// <summary>
    /// The account's login.
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Login { get; set; }

    /// <summary>
    /// The account's full name.
    /// </summary>
    [StringLength(255)]
    public string Name { get; set; }

    public GitHubMetaData GitHubMetaData { get; set; } = new GitHubMetaData();

    public VersionMetaData VersionMetaData { get; set; } = new VersionMetaData();

    public virtual AccessToken AccessToken { get; set; }

    public virtual ICollection<Repository> Repositories { get; set; } = new HashSet<Repository>();

    public virtual ICollection<AuthenticationToken> AuthenticationTokens { get; set; } = new HashSet<AuthenticationToken>();
  }
}
