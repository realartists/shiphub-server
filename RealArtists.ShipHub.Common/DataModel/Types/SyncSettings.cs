namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Collections.Generic;

  public class SyncSettings {
    public bool AutoTrack { get; set; } = true;
    public IEnumerable<long> Include { get; set; } = Array.Empty<long>();
    public IEnumerable<long> Exclude { get; set; } = Array.Empty<long>();
  }
}
