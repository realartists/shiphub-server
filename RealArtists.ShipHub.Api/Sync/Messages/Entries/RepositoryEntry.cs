namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;

  public class RepositoryEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Owner { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }

    public IEnumerable<long> Assignees { get; set; } = Array.Empty<long>();
    public IEnumerable<Label> Labels { get; set; } = Array.Empty<Label>();
  }
}
