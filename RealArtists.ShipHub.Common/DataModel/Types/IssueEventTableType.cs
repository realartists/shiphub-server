﻿namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class IssueEventTableType {
    public long Id { get; set; }
    public long IssueId { get; set; }
    public long ActorId { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? AssigneeId { get; set; }
    public string ExtensionData { get; set; }
  }
}
