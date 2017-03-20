using System;
using Newtonsoft.Json;

namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  public class ProjectEntry : SyncEntity {
    public long Identifier { get; set; }
    public string Name { get; set; }
    public long Number { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Creator { get; set; }
    [JsonProperty(NullValueHandling =NullValueHandling.Ignore)]
    public long? Organization { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public long? Repository { get; set; }
  }
}