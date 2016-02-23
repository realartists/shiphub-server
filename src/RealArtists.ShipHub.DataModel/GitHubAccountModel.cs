namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("Accounts", Schema = "GitHub")]
  public class GitHubAccountModel {
    /// <summary>
    /// The account's GitHub unique ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// URL of the account's avatar.
    /// </summary>
    [StringLength(500)]
    public string AvatarUrl { get; set; }

    /// <summary>
    /// Company the account works for.
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Company { get; set; }

    /// <summary>
    /// Date the account was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The account's login.
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Login { get; set; }

    /// <summary>
    /// The account's full name.
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Name { get; set; }

    public virtual ICollection<GitHubAuthenticationTokenModel> AuthenticationTokens { get; set; } = new HashSet<GitHubAuthenticationTokenModel>();

    public virtual ICollection<GitHubRepositoryModel> Repositories { get; set; } = new HashSet<GitHubRepositoryModel>();
  }
}
