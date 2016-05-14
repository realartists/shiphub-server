namespace RealArtists.Ship.Server.QueueClient.Messages {
  using RealArtists.ShipHub.Common.GitHub.Models;

  public class SyncAccountRepositoriesMessage {
    public string AccessToken { get; set; }
    public Account Account { get; set; }
  }
}
