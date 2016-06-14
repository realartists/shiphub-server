namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class IssueEvent {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long RepositoryId { get; set; }

    //public long ActorId { get; set; }

    //public long AssigneeId { get; set; }

    //public long AssignerId { get; set; }

    //[StringLength(40)]
    //public string CommitId { get; set; }

    //[Required]
    //[StringLength(64)]
    //public string Type { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    //[StringLength(10)]
    //public string LabelColor { get; set; }

    //[StringLength(150)]
    //public string LabelName { get; set; }

    //public long? MilestoneId { get; set; }

    //public int? MilestoneNumber { get; set; }

    //[StringLength(10)]
    //public string MilestoneState { get; set; }

    //[StringLength(255)]
    //public string MilestoneTitle { get; set; }

    //[StringLength(255)]
    //public string MilestoneDescription { get; set; }

    //public DateTimeOffset? MilestoneCreatedAt { get; set; }

    //public DateTimeOffset? MilestoneUpdatedAt { get; set; }

    //public DateTimeOffset? MilestoneClosedAt { get; set; }

    //public DateTimeOffset? MilestoneDueOn { get; set; }

    //[StringLength(255)]
    //public string RenameFrom { get; set; }

    //[StringLength(255)]
    //public string RenameTo { get; set; }

    //public virtual User Actor { get; set; }

    //public virtual User Assignee { get; set; }

    //public virtual Milestone Milestone { get; set; }

    [Required]
    public string ExtensionData { get; set; }

    public virtual Repository Repository { get; set; }
  }
}