namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;

  public class RepositoryEntry {
    public long Identifier { get; set; }
    public long AccountIdentifier { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public bool Hidden { get; set; }
  }
}