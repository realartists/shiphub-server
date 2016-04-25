namespace RealArtists.ShipHub.Common.DataModel {
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;

  public class User : Account {
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Organization> Organizations { get; set; } = new HashSet<Organization>();
  }
}
