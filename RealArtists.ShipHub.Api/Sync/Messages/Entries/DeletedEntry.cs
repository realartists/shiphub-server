namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Diagnostics.CodeAnalysis;

  public class DeletedEntry : SyncEntity {
    public long Identifier { get; }

    public DeletedEntry(long identifier) {
      Identifier = identifier;
    }
  }

  public class DeletedGuidEntry : SyncEntity {
    public string Identifier { get; }

    [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
    public DeletedGuidEntry(Guid identifier) {
      Identifier = identifier.ToString().ToLowerInvariant(); // Be particular about formatting - the mobile client cares.
    }
  }
}
