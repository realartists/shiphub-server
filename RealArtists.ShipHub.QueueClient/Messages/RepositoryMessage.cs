namespace RealArtists.ShipHub.QueueClient.Messages {
  using Common.GitHub.Models;

  public class RepositoryMessage : AccessTokenMessage {
    public Repository Repository { get; set; }
  }
}
