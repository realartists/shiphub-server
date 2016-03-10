namespace RealArtists.ShipHub.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class Repository : IGitHubResource, IVersionedResource {
    public string TopicName { get { return FullName; } }

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    public int AccountId { get; set; }

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

    public GitHubMetaData GitHubMetaData { get; set; } = new GitHubMetaData();

    public VersionMetaData VersionMetaData { get; set; } = new VersionMetaData();

    public virtual Account Account { get; set; }
  }
}
