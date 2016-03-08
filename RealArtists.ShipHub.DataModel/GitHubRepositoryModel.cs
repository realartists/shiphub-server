namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("Repositories", Schema = "GitHub")]
  public class GitHubRepositoryModel : GitHubApiResource {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    public int OwnerId { get; set; }

    public bool Private { get; set; }

    public bool HasIssues { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; }

    [Required]
    [StringLength(500)]
    public string FullName { get; set; }

    [Required(AllowEmptyStrings = true)]
    [StringLength(500)]
    public string Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual GitHubAccountModel Owner { get; set; }
  }
}
