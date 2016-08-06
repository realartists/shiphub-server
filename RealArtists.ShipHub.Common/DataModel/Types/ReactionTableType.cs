namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class ReactionTableType {
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
  }
}
