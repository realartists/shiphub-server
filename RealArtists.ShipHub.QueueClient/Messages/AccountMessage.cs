namespace RealArtists.ShipHub.QueueClient.Messages {
  public class AccountMessage {
    public AccountMessage() { }

    public AccountMessage(long id, string login, string accessToken) {
      Id = id;
      Login = login;
      AccessToken = accessToken;
    }

    public long Id { get; set; }
    public string Login { get; set; }
    public string AccessToken { get; set; }
  }
}
