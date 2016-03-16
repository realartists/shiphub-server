namespace RealArtists.ShipHub.Api.Models {
  public class ApiRelationship {
    public int ParentIdentifier { get; set; }
    public int ChildIdentifier { get; set; }
    public string Type { get; set; }
  }
}