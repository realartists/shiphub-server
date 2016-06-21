namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class IssueEvent {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long RepositoryId { get; set; }

    public long IssueId { get; set; }

    public long ActorId { get; set; }

    [StringLength(40)]
    public string CommitId { get; set; }

    [Required]
    [StringLength(64)]
    public string Event { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? AssigneeId { get; set; }

    //public long? AssignerId { get; set; }

    public long? MilestoneId { get; set; }

    public virtual Repository Repository { get; set; }

    public virtual Issue Issue { get; set; }

    public virtual User Actor { get; set; }

    public virtual User Assignee { get; set; }

    public virtual Milestone Milestone { get; set; }

    [Required]
    public string ExtensionData { get; set; }
  }
}