namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System.Collections.Generic;

  public class RepositoryEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Owner { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }

    public IEnumerable<long> Assignees { get; set; }
    public IEnumerable<Label> Labels { get; set; }
  }
}
