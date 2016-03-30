namespace RealArtists.ShipHub.DataModel {
  public interface IVersionedResource {
    string TopicName { get; }
    long RowVersion { get; set; }
  }
}
