namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Collections.Generic;
  using System.Text;
  using Hashing;

  public class IssueEventTableType {
    public long Id { get; set; }
    public long IssueId { get; set; }
    public long? ActorId { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ExtensionData { get; set; }

    public Guid? Hash {
      get {
        using (var hashFunction = new MurmurHash3()) {
          var hash = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(ExtensionData));
          return new Guid(hash);
        }
      }
    }

    ///////////////////////////////////
    // ACL Helpers
    ///////////////////////////////////

    // restricted events: closed, referenced, cross-referenced
    private static HashSet<string> _PublicEvents { get; } = new HashSet<string>(
      new[] { "reopened", "subscribed", "mentioned", "assigned", "unassigned", "labeled", "unlabeled", "milestoned",
              "demilestoned", "renamed", "locked", "unlocked", "merged", "head_ref_deleted", "head_ref_restored",
              "commented", "committed", "reopened", },
      StringComparer.OrdinalIgnoreCase);
    public bool Restricted { get { return !_PublicEvents.Contains(Event); } }
  }
}
