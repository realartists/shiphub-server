namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System.Collections.Generic;

  public class SyncSettings {
    public bool AutoTrack { get; set; }
    public IEnumerable<long> Include { get; set; }
    public IEnumerable<long> Exclude { get; set; }
  }
}
