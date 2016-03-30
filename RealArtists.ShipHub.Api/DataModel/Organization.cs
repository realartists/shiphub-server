namespace RealArtists.ShipHub.Api.DataModel {
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;

  public class Organization : Account {
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<User> Members { get; set; } = new HashSet<User>();
  }
}
