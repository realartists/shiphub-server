namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class ProjectTableType {
    public long Id { get; set; }
    public string Name { get; set; }
    public long Number { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long CreatorId { get; set; }
  }
}
