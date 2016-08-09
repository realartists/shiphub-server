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

    [Required]
    [StringLength(64)]
    public string Event { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? Hash { get; set; }

    public bool Restricted { get; set; }

    public bool Timeline { get; set; }

    [Required]
    public string ExtensionData { get; set; }

    public virtual Repository Repository { get; set; }

    public virtual Issue Issue { get; set; }

    public virtual Account Actor { get; set; }
  }
}
