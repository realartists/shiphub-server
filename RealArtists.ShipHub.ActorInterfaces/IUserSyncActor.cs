namespace RealArtists.ShipHub.ActorInterfaces {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;

  /// <summary>
  /// Represents a user. Currently only handles sync, but will ideally manage changes pub-sub as well.
  /// </summary>
  public interface IUserSyncActor : Orleans.IGrainWithIntegerKey {

  }
}
