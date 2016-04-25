namespace RealArtists.ShipHub.Common.DataModel {
  public interface IVersionedResource {
    string TopicName { get; }
    long RowVersion { get; set; }
  }
}
