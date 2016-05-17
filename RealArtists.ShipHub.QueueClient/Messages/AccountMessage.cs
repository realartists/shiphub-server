namespace RealArtists.ShipHub.QueueClient.Messages {
  using Common.GitHub.Models;

  public class AccountMessage : AccessTokenMessage {
    public Account Account { get; set; }
  }
}
