namespace RealArtists.ShipHub.Api.Models {
  using System;
  using System.Collections.Generic;

  public class ApiIssue {
    public string Body { get; set; }
    public bool Closed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Identifier { get; set; }
    public bool Locked { get; set; }
    public int Number { get; set; }
    public string Title { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? AssigneeIdentifier { get; set; }
    public long? ClosedByIdentifier { get; set; }
    public IEnumerable<int> Labels { get; set; }
    public long? OriginatorIdentifier { get; set; }
    public long RepositoryIdentifier { get; set; }
  }
}