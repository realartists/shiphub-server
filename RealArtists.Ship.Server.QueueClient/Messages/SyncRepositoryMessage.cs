namespace RealArtists.Ship.Server.QueueClient.Messages {
  using RealArtists.ShipHub.Common.GitHub.Models;

  public class SyncRepositoryMessage {
    public string AccessToken { get; set; }
    public Repository Repository { get; set; }
  }
}
