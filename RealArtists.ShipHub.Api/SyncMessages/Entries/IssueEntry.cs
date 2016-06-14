namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;

  public class IssueEntry : SyncEntity {
    public long Identifier { get; set; }
    public long UserIdentifier { get; set; }
    public long RepositoryIdentifier { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public long? AssigneeIdentifier { get; set; }
    public long? MilestoneIdentifier { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedByIdentifier { get; set; }
    public Reactions Reactions { get; set; }

    public IEnumerable<Label> Labels { get; set; }
  }
}
