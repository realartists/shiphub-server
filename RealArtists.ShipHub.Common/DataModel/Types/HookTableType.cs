namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class HookTableType {
    public long Id { get; set; }
    public long? GitHubId { get; set; }
    public Guid Secret { get; set; }
    public string Events { get; set; }
    public DateTimeOffset? LastError { get; set; }
  }
}
