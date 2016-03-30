namespace RealArtists.ShipHub.Api.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public partial class Comment {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    public int IssueId { get; set; }

    public int RepositoryId { get; set; }

    public int UserId { get; set; }

    [Required]
    public string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [Required]
    public string ExtensionJson { get; set; }

    public long? MetaDataId { get; set; }

    public long? RowVersion { get; set; }

    public virtual User User { get; set; }

    public virtual Issue Issue { get; set; }

    public virtual Repository Repository { get; set; }

    public virtual GitHubMetaData MetaData { get; set; }
  }
}