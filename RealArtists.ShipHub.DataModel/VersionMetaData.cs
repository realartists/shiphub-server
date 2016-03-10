namespace RealArtists.ShipHub.DataModel {
  using System.ComponentModel.DataAnnotations.Schema;

  public interface IVersionedResource {
    string TopicName { get; }
    VersionMetaData VersionMetaData { get; set; }
  }

  /// <summary>
  /// Common tracking data used to sync with clients.
  /// </summary>
  [ComplexType]
  public class VersionMetaData {
    //[ConcurrencyCheck]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public long RowVersion { get; set; }

    public long? RestoreVersion { get; set; }
  }
}
