namespace RealArtists.ShipHub.QueueClient.Messages {
  using System.Collections.Generic;

  public class SyncOrgSubscriptionStateMessage {
    public SyncOrgSubscriptionStateMessage() { }

    public SyncOrgSubscriptionStateMessage(IEnumerable<long> orgIds, long forUserId) {
      OrgIds = orgIds;
      ForUserId = forUserId;
    }

    public IEnumerable<long> OrgIds { get; set; }
    public long ForUserId { get; set; }

    public override string ToString() {
      return $"SyncOrgSubscriptionStateMessage {{ OrgIds: {string.Join(", ", OrgIds)} ForUserId: {ForUserId} }}";
    }
  }
}
