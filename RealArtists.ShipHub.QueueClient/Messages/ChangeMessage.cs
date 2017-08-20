namespace RealArtists.ShipHub.QueueClient.Messages {
  using System.Collections.Generic;
  using System.Linq;
  using Common.DataModel.Types;

  public class ChangeMessage : IChangeSummary {
    public ChangeMessage() { }
    public ChangeMessage(IChangeSummary changes) : this(changes, false) { }
    public ChangeMessage(IChangeSummary changes, bool urgent) {
      IsUrgent = urgent;
      // Copy for safety from underlying modifications.
      Organizations = changes.Organizations.ToArray();
      Repositories = changes.Repositories.ToArray();
      Users = changes.Users.ToArray();
    }

    public bool IsUrgent { get; set; } = false;
    public IEnumerable<long> Organizations { get; set; }
    public IEnumerable<long> Repositories { get; set; }
    public IEnumerable<long> Users { get; set; }

    public bool IsEmpty => Organizations?.Any() == false && Repositories?.Any() == false && Users?.Any() == false;

    public override string ToString() {
      return $"ChangeMessage {{ Organizations: [{string.Join(", ", Organizations)}] Repositories: [{string.Join(", ", Repositories)}] Users: [{string.Join(", ", Users)}] Urgent: {IsUrgent} }}";
    }
  }
}
