namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class Repository : GitHubResource {
    public override string TopicName { get { return FullName; } }

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

    [StringLength(500)]
    [Column("Description")]
    [Required(AllowEmptyStrings = true)]
    public string RepoDescription { get; set; }

    public virtual Account Account { get; set; }

    public virtual ICollection<User> SubscribedUsers { get; set; } = new HashSet<User>();
  }
}
