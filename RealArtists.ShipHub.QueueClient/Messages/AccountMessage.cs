namespace RealArtists.ShipHub.QueueClient.Messages {
  using Common.GitHub.Models;

  public interface IAccountMessage {
    Account Account { get; }
  }

  public class AccountMessage : IAccessTokenMessage {
    public Account Account { get; set; }
    public string AccessToken { get; set; }
  }
}
