namespace RealArtists.ShipHub.QueueClient.Messages {
  public interface IAccessTokenMessage {
    string AccessToken { get; }
  }

  public class AccessTokenMessage : IAccessTokenMessage {
    public string AccessToken { get; set; }
  }
}
