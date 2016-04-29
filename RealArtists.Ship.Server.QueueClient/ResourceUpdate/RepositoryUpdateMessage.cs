namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  using ShipHub.Common.GitHub.Models;

  public class RepositoryUpdateMessage : UpdateMessage<Repository> {
    public int AccountId { get; set; }
  }
}
