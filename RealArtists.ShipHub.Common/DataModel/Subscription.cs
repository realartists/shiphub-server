namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  /// <summary>
  /// The set of subscription states that Ship's server and client care
  /// about.  Not a direct mapping to ChargeBee's subscription states.
  /// </summary>
  public enum SubscriptionState {
    /// <summary>
    /// Customer never had a paying subscription, or their subscription
    /// was cancelled, or their trial lapsed.
    /// </summary>
    NoSubscription = 0,

    /// <summary>
    /// Customer is still in the trial period and has not yet provided
    /// payment info.  If no action is taken, subscription will cancel
    /// at end of the trial.
    /// </summary>
    InTrial = 1,

    /// <summary>
    /// Customer is currently paying, or has provided payment info and is
    /// scheduled to pay as soon as their trial ends.
    /// </summary>
    Subscribed = 2,
  }

  public class Subscription {
    [Required]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Key]
    public long AccountId { get; set; }
    public virtual Account Account { get; set; }

    [Required]
    public SubscriptionState State { get; set; }

    public DateTimeOffset TrialEndDate { get; set; }
  }
}
