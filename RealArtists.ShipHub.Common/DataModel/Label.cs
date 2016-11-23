namespace RealArtists.ShipHub.Common.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  public class Label {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    [Required]
    public long RepositoryId { get; set; }

    [Required]
    [Column(TypeName = "char")]
    [StringLength(6, MinimumLength = 6)]
    public string Color { get; set; }

    [Required]
    [StringLength(400)]
    public string Name { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; } = new HashSet<Issue>();

    public virtual Repository Repository { get; set; }
  }
}