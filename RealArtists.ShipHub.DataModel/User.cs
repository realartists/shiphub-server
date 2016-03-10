namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;

  public class User : Account {
    public virtual ICollection<Organization> Organizations { get; set; }
  }
}
