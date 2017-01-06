namespace RealArtists.ShipHub.QueueClient.Messages {
  public class TargetMessage {
    public TargetMessage() { }

    public TargetMessage(long targetId, long forUserId) {
      TargetId = targetId;
      ForUserId = forUserId;
    }

    public long TargetId { get; set; }
    public long ForUserId { get; set; }

    public override string ToString() {
      return $"TargetMessage {{ TargetId: {TargetId} ForUserId: {ForUserId} }}";
    }
  }
}
