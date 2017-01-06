namespace RealArtists.ShipHub.QueueClient.Messages {
  using System.Collections.Generic;
  using System.Linq;
  using Common.DataModel.Types;

  public class ChangeMessage: IChangeSummary {
    public ChangeMessage() { }
    public ChangeMessage(IChangeSummary changes) {
      // Copy for safety from underlying modifications.
      Organizations = changes.Organizations.ToArray();
      Repositories = changes.Repositories.ToArray();
      Users = changes.Users.ToArray();
    }

    public IEnumerable<long> Organizations { get; set; }
    public IEnumerable<long> Repositories { get; set; }
    public IEnumerable<long> Users { get; set; }

    public override string ToString() {
      return $"ChangeMessage {{ Organizations: [{string.Join(", ", Organizations)}] Repositories: [{string.Join(", ", Repositories)}] Users: [{string.Join(", ", Users)}] }}";
    }
  }
}
