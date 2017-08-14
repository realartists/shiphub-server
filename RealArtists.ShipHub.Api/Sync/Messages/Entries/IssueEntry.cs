namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;

  public class IssueEntry : SyncEntity {
    public long Identifier { get; set; }
    public long User { get; set; }
    public long Repository { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public long? Milestone { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public bool PullRequest { get; set; }
    public ReactionSummary ShipReactionSummary { get; set; }

    public IEnumerable<long> Assignees { get; set; }
    public IEnumerable<long> Labels { get; set; }
    public IEnumerable<long> Mentions { get; set; }
  }
}
