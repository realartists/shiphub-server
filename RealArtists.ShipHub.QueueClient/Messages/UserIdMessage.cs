namespace RealArtists.ShipHub.QueueClient.Messages {
  public class UserIdMessage {
    public UserIdMessage() { }

    public UserIdMessage(long userId) {
      UserId = userId;
    }

    public long UserId { get; set; }

    public override string ToString() {
      return $"UserIdMessage {{${UserId}}}";
    }
  }
}
