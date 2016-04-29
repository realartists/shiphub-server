namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  using ShipHub.Common.GitHub.Models;

  public class MilestoneUpdateMessage : UpdateMessage<Milestone> {
    public int RepositoryId { get; set; }
  }
}
