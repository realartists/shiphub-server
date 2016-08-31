namespace RealArtists.ShipHub.QueueClient.Messages {
  public class AccountMessage {
    public AccountMessage() { }

    public AccountMessage(long targetId, string targetLogin, long forUserId) {
      TargetId = targetId;
      TargetLogin = targetLogin;
      ForUserId = forUserId;
    }

    public long TargetId { get; set; }
    public string TargetLogin { get; set; }
    public long ForUserId { get; set; }
  }
}
