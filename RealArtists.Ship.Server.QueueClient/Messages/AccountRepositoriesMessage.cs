namespace RealArtists.Ship.Server.QueueClient.Messages {
  using System.Collections.Generic;

  public class AccountRepositoriesMessage {
    public int AccountId { get; set; }
    public IEnumerable<int> LinkedRepositoryIds { get; set; }
  }
}
