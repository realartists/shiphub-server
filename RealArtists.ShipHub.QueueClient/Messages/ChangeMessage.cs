namespace RealArtists.ShipHub.QueueClient.Messages {
  using System.Collections.Generic;
  using System.Linq;
  using Common.DataModel.Types;

  public class ChangeMessage {
    public ChangeMessage() { }
    public ChangeMessage(IChangeSummary changes) {
      // Copy for safety from underlying modifications.
      Organizations = changes.Organizations.ToArray();
      Repositories = changes.Repositories.ToArray();
    }

    public IEnumerable<long> Organizations { get; set; }
    public IEnumerable<long> Repositories { get; set; }
  }
}
