namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using Newtonsoft.Json;

  public class QueryEntry : SyncEntity {
    [JsonIgnore]
    public Guid Id { get; set; }

    [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
    public string Identifier => Id.ToString().ToLowerInvariant(); // Be particular about formatting - the mobile client cares.
    public AccountEntry Author { get; set; }
    public string Title { get; set; }
    public string Predicate { get; set; }
  }
}
