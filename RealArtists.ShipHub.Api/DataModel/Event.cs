namespace RealArtists.ShipHub.Api.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public partial class Event {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    public int ActorId { get; set; }

    public int AssigneeId { get; set; }

    public int AssignerId { get; set; }

    [StringLength(40)]
    public string CommitId { get; set; }

    [Required]
    [StringLength(64)]
    public string Type { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [StringLength(10)]
    public string LabelColor { get; set; }

    [StringLength(150)]
    public string LabelName { get; set; }

    public int? MilestoneId { get; set; }

    public int? MilestoneNumber { get; set; }

    [StringLength(10)]
    public string MilestoneState { get; set; }

    [StringLength(255)]
    public string MilestoneTitle { get; set; }

    [StringLength(255)]
    public string MilestoneDescription { get; set; }

    public DateTimeOffset? MilestoneCreatedAt { get; set; }

    public DateTimeOffset? MilestoneUpdatedAt { get; set; }

    public DateTimeOffset? MilestoneClosedAt { get; set; }

    public DateTimeOffset? MilestoneDueOn { get; set; }

    [StringLength(255)]
    public string RenameFrom { get; set; }

    [StringLength(255)]
    public string RenameTo { get; set; }

    [Required]
    public string ExtensionJson { get; set; }

    public virtual User Actor { get; set; }

    public virtual User Assignee { get; set; }

    public virtual Milestone Milestone { get; set; }

    public virtual Repository Repository { get; set; }
  }
}