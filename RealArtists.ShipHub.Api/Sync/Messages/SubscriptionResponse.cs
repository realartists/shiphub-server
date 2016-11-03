namespace RealArtists.ShipHub.Api.Sync.Messages {
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

  public class SubscriptionResponse : SyncMessageBase {
    public override string MessageType { get; set; } = "subscription";
    public SubscriptionMode Mode { get; set; }
    public DateTimeOffset? TrialEndDate { get; set; }

    /// <summary>
    /// The client will refresh the "Manage Subscriptions" window whenever
    /// this hash changes value.
    /// </summary>
    public string ManageSubscriptionsRefreshHash { get; set; }
  }
}
