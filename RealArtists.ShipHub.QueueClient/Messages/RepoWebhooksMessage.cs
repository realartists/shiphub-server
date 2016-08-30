namespace RealArtists.ShipHub.QueueClient.Messages {

  /// <summary>
  /// TODO: Once the incremental sync branch lands, figure out if we can re-use
  /// one of the existing messages.  For now, I'm making my own so I can avoid rebase
  /// hell.
  /// </summary>
  public class RepoWebhooksMessage {
    public long RepositoryId { get; set; }
    public long UserId { get; set; }
  }
}
