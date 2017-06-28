namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Collections.Generic;
  using System.Text;
  using Hashing;

  public class IssueEventTableType {
    public string UniqueKey { get; set; }
    public long? Id { get; set; }
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

    private static HashSet<string> _PublicEvents { get; } = new HashSet<string>(new[] {
      "assigned",
      // "closed", (can have private commit info)
      "commented", // filtered, but would be public
      "committed", // only to the PR branch (safe)
      "converted_note_to_issue",
      // "cross-referenced", (a comment or issue body in possibly another repo, that refers to this issue)
      "demilestoned",
      "head_ref_deleted", // can only be in the PR's repo, so should be safe to mark as public
      "head_ref_restored", // can only be in the PR's repo, so should be safe to mark as public
      "labeled",
      "locked",
      "mentioned", // filtered, but would be public
      "merged", // can only be in the PR's repo, so should be safe to mark as public
      "milestoned",
      // "referenced", (a commit in a repo, not necessarily the issue's repo)
      "renamed",
      "reopened",
      "review_dismissed",
      "review_request_removed",
      "review_requested",
      "reviewed",
      "subscribed", // filtered, but would be public
      "unassigned",
      "unlabeled",
      "unlocked",
    }, StringComparer.OrdinalIgnoreCase);
    public bool Restricted => !_PublicEvents.Contains(Event);
  }
}
