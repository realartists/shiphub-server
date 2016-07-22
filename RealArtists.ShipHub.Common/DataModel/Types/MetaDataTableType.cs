namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class MetaDataTableType {
    public long ItemId { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastRefresh { get; set; }
    public long? AccountId { get; set; }
  }
}
