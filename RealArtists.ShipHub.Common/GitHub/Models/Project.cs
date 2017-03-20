using System;

namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class Project {
    public long Id { get; set; }
    public string Name { get; set; }
    public long Number { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Account Creator { get; set; }
  }
}
