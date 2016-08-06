namespace RealArtists.ShipHub.QueueClient.Messages {
  public class IssueCommentMessage : IAccessTokenMessage {
    public long CommentId { get; set; }
    public string AccessToken { get; set; }
  }
}
