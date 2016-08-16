namespace RealArtists.ShipHub.QueueClient.Messages {
  public class AccessTokenMessage {
    public AccessTokenMessage() { }

    public AccessTokenMessage(string accessToken) {
      AccessToken = accessToken;
    }

    public string AccessToken { get; set; }
  }
}
