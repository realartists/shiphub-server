namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;

  public class Hook {
    public long Id { get; set; }
    public long? GitHubId { get; set; }

    public Guid Secret { get; set; }

    [Required]
    [StringLength(500)]
    public string Events { get; set; }

    public long? RepositoryId { get; set; }
    public long? OrganizationId { get; set; }

    /// <summary>
    /// Used to track troublesome hooks, for example, when there are over 20 push hooks.
    /// </summary>
    public DateTimeOffset? LastError { get; set; }

    public virtual Repository Repository { get; set; }
    public virtual Organization Organization { get; set; }
  }
}
