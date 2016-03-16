namespace RealArtists.ShipHub.DataModel {
  using System.Collections.Generic;

  public class User : Account {
    public virtual ICollection<Organization> Organizations { get; set; } = new HashSet<Organization>();

    public virtual ICollection<Repository> SubscribedRepositories { get; set; } = new HashSet<Repository>();
  }
}
