namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;

  public class Organization : Account {
    public virtual ICollection<User> Members { get; set; }
  }
}
