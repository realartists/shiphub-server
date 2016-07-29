namespace RealArtists.ShipHub.QueueClient.Messages {

  /// <summary>
  /// TODO: Once the incremental sync branch lands, figure out if we can re-use
  /// one of the existing messages.  For now, I'm making my own so I can avoid rebase
  /// hell.
  /// </summary>
  public class AddOrUpdateRepoWebHooksMessage {
    public long RepositoryId { get; set; }
    public string AccessToken { get; set; }
  }
}
