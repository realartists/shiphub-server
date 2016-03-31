﻿namespace RealArtists.ShipHub.Api.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  public partial class Milestone : IGitHubResource {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    public int UserId { get; set; }

    public int Number { get; set; }

    [Required]
    [StringLength(10)]
    public string State { get; set; }

    [Required]
    [StringLength(255)]
    public string Title { get; set; }

    [Required]
    [StringLength(255)]
    public string Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset? DueOn { get; set; }

    [Required]
    public string ExtensionJson { get; set; }

    public long? MetaDataId { get; set; }

    public long? RowVersion { get; set; }

    public virtual User User { get; set; }

    public virtual GitHubMetaData MetaData { get; set; }

    public virtual Repository Repository { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Event> Events { get; set; } = new HashSet<Event>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; } = new HashSet<Issue>();
  }
}