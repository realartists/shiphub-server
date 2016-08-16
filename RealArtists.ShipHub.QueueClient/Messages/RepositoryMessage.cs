namespace RealArtists.ShipHub.QueueClient.Messages {

  public class RepositoryMessage {
    public RepositoryMessage() { }

    public RepositoryMessage(long id, string fullName, string accessToken) {
      Id = id;
      FullName = fullName;
      AccessToken = accessToken;
    }

    public long Id { get; set; }
    public string FullName { get; set; }
    public string AccessToken { get; set; }
  }
}
