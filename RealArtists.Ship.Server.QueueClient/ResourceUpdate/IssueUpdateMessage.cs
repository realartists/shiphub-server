namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  using ShipHub.Common.GitHub.Models;

  public class IssueUpdateMessage : UpdateMessage<Issue> {
    public int RepositoryId { get; set; }
  }
}
