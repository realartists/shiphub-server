namespace RealArtists.ShipHub.QueueClient.Messages {
  public class IssueViewMessage {
    public IssueViewMessage() { }

    public IssueViewMessage(long userId, string repoFullName, int issueNumber) {
      RepositoryFullName = repoFullName;
      Number = issueNumber;
      UserId = userId;
    }

    public int Number { get; set; }
    public string RepositoryFullName { get; set; }
    public long UserId { get; set; }
  }
}
