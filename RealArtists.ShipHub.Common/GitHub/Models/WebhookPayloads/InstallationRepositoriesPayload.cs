namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  using System.Collections.Generic;

  public class InstallationRepositoriesPayload {
    public string Action { get; set; }
    public Installation Installation { get; set; }
    public string RepositorySelection { get; set; }
    public IEnumerable<Repository> RepositoriesAdded { get; set; }
    public IEnumerable<Repository> RepositoriesRemoved { get; set; }
    public Account Sender { get; set; }
  }
}
