namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System.Collections.Generic;

  public class RepositoryEntry : SyncEntity {
    public long Identifier { get; set; }
    public long AccountIdentifier { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }

    public IEnumerable<Label> Labels { get; set; }
  }
}
