namespace RealArtists.ShipHub.QueueClient.Messages {
  public class IssueMessage : IAccessTokenMessage {
    public int Number { get; set; }
    public string RepositoryFullName { get; set; }
    public string AccessToken { get; set; }
  }
}
