namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;

  public class Organization : Account {
    public virtual ICollection<User> Users { get; set; } = new HashSet<User>();
  }
}
