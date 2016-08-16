namespace RealArtists.ShipHub.QueueClient.Messages {
  public class IssueMessage {
    public IssueMessage() { }

    public IssueMessage(string repoFullName, int issueNumber, string accessToken) {
      RepositoryFullName = repoFullName;
      Number = issueNumber;
      AccessToken = accessToken;
    }

    public int Number { get; set; }
    public string RepositoryFullName { get; set; }
    public string AccessToken { get; set; }
  }
}
