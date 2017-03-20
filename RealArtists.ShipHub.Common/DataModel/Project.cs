namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class Project {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    [Required]
    public string Name { get; set; }
    public long Number { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long CreatorId { get; set; }
    public long? OrganizationId { get; set; }
    public long? RepositoryId { get; set; }

    public virtual Account Creator { get; set; }
    public virtual Repository Repository { get; set; }
    public virtual Organization Organization { get; set; }
  }
}
