namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System.Collections.Generic;

  public class OrganizationEntry : AccountEntry {
    public IEnumerable<long> Users { get; set; }
  }
}
