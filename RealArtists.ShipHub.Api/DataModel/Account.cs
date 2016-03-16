namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public abstract class Account : GitHubResource {
    public const string OrganizationType = "org";
    public const string UserType = "user";

    public override string TopicName { get { return Login; } }

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [StringLength(500)]
    public string AvatarUrl { get; set; }

    [Required]
    [StringLength(255)]
    public string Login { get; set; }

    [StringLength(255)]
    public string Name { get; set; }

    public virtual AccessToken AccessToken { get; set; }

    public virtual ICollection<Repository> Repositories { get; set; } = new HashSet<Repository>();

    public virtual ICollection<AuthenticationToken> AuthenticationTokens { get; set; } = new HashSet<AuthenticationToken>();
  }
}
