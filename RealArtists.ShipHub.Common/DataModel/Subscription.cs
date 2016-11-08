namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Runtime.Serialization;

  /// <summary>
  /// The set of subscription states that Ship's server and client care
  /// about.  Not a direct mapping to ChargeBee's subscription states.
  /// </summary>
  public enum SubscriptionState {
    [EnumMember(Value = "unknown")]
    Unknown = 0,

    /// <summary>
    /// Customer never had a paying subscription, or their subscription
    /// was cancelled, or their trial lapsed.
    /// </summary>
    [EnumMember(Value = "not_subscribed")]
    NotSubscribed,

    /// <summary>
    /// Customer is still in the trial period and has not yet provided
    /// payment info.  If no action is taken, subscription will cancel
    /// at end of the trial.
    /// </summary>
    [EnumMember(Value = "in_trial")]
    InTrial,

    /// <summary>
    /// Customer is currently paying.
    /// </summary>
    [EnumMember(Value = "subscribed")]
    Subscribed,
  }

  public class Subscription {
    [Required]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Key]
    public long AccountId { get; set; }
    public virtual Account Account { get; set; }
    
    [NotMapped]
    public SubscriptionState State { get; set; }

    [Required]
    [StringLength(15)]
    [Column("State")]
    public string StateName {
      get { return State.ToString(); }
      set { State = (SubscriptionState)Enum.Parse(typeof(SubscriptionState), value); }
    }

    public DateTimeOffset? TrialEndDate { get; set; }

    [ConcurrencyCheck]
    public long Version { get; set; }
  }
}
