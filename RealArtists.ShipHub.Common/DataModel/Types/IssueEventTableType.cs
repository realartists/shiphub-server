namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class IssueEventTableType {
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ExtensionData { get; set; }
  }
}
