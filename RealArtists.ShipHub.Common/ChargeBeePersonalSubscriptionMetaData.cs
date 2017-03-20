namespace RealArtists.ShipHub.Common {
  /// <summary>
  /// ChargeBeePersonalSubscriptionMetaData objects get serialized and stored with
  /// each subscription in ChargeBee.  If changing the schema, be aware that
  /// older versions of this object still exist in ChargeBee.
  /// </summary>
  public class ChargeBeePersonalSubscriptionMetadata {
    public int? TrialPeriodDays { get; set; }
  }
}
