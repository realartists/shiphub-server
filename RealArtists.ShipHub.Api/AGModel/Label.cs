namespace RealArtists.ShipHub.Api.AGModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.Diagnostics.CodeAnalysis;

  public partial class Label {
    [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    public Label() {
      Issues = new HashSet<Issue>();
      Repositories = new HashSet<Repository>();
    }

    public long Id { get; set; }

    [Required]
    [StringLength(6)]
    public string Color { get; set; }

    [Required]
    [StringLength(150)]
    public string Name { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> Repositories { get; set; }
  }
}
