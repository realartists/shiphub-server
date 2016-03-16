namespace RealArtists.ShipHub.Api.Models {
  using System;
  using System.Collections.Generic;

  public class ApiIssue {
    public string Body { get; set; }
    public bool Closed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Identifier { get; set; }
    public bool Locked { get; set; }
    public int Number { get; set; }
    public string Title { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? AssigneeIdentifier { get; set; }
    public int? ClosedByIdentifier { get; set; }
    public IEnumerable<int> Labels { get; set; }
    public int? OriginatorIdentifier { get; set; }
    public int RepositoryIdentifier { get; set; }
  }
}