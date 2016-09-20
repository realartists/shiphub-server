namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Runtime.Serialization;

  public enum SubscriptionMode {
    [EnumMember(Value = "unknown")]
    Unknown = 0,

    [EnumMember(Value = "paid")]
    Paid,

    [EnumMember(Value = "trial")]
    Trial,

    [EnumMember(Value = "free")]
    Free
  }

  public class SubscriptionEntry : SyncEntity {
    public SubscriptionMode Mode { get; set; }
    public DateTimeOffset? TrialEndDate { get; set; }
  }
}