namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System.Collections.Generic;

  public class OrganizationEntry : AccountEntry {
    public IEnumerable<long> Users { get; set; }
  }
}
