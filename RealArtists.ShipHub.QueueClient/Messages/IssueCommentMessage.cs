namespace RealArtists.ShipHub.QueueClient.Messages {
  public class IssueCommentMessage {
    public IssueCommentMessage() { }

    public IssueCommentMessage(long commentId, string accessToken) {
      CommentId = commentId;
      AccessToken = accessToken;
    }

    public long CommentId { get; set; }
    public string AccessToken { get; set; }
  }
}
