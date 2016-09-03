namespace RealArtists.ShipHub.QueueClient.Messages {
  public class IssueViewMessage {
    public IssueViewMessage() { }

    public IssueViewMessage(string repoFullName, int issueNumber, long forUserId) {
      RepositoryFullName = repoFullName;
      Number = issueNumber;
      ForUserId = forUserId;
    }

    public int Number { get; set; }
    public string RepositoryFullName { get; set; }
    public long ForUserId { get; set; }
  }
}
