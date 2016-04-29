namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  using ShipHub.Common.GitHub.Models;

  public class CommentUpdateMessage : UpdateMessage<Comment> {
    public int IssueId { get; set; }
    public int RepositoryId { get; set; }
  }
}
