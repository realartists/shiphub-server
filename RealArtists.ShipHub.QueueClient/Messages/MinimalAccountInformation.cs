namespace RealArtists.ShipHub.QueueClient.Messages {
  using ShipHub.Common.GitHub.Models;

  public class MinimalAccountInformation {
    public int Id { get; set; }
    public GitHubAccountType Type { get; set; }
    public string Login { get; set; }
  }
}
