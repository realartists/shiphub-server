namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;

  public class OrganizationEntry : AccountEntry {
    public bool ShipNeedsWebhookHelp { get; set; }
    public IEnumerable<long> Users { get; set; } = Array.Empty<long>();
  }
}
