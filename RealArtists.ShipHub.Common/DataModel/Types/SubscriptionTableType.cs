namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class SubscriptionTableType {
    public long AccountId { get; set; }
    public string State { get; set; }
    public DateTimeOffset? TrialEndDate { get; set; }
    public long Version { get; set; }
  }
}
