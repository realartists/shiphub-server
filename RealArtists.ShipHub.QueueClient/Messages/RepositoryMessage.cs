namespace RealArtists.ShipHub.QueueClient.Messages {
  using Common.GitHub.Models;

  public interface IRepositoryMessage {
    Repository Repository { get; }
  }

  public class RepositoryMessage : IAccessTokenMessage {
    public Repository Repository { get; set; }
    public string AccessToken { get; set; }
  }
}
