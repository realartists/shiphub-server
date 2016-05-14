namespace RealArtists.ShipHub.QueueClient.Messages {
  using System.Collections.Generic;
  using ShipHub.Common.GitHub.Models;

  public class RepositoryAssignableMessage {
    public Repository Repository { get; set; }
    public IEnumerable<MinimalAccountInformation> AssignableAccounts { get; set; }
  }
}
