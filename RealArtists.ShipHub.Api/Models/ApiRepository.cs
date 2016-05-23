namespace RealArtists.ShipHub.Api.Models {
  public class ApiRepository {
    public long Identifier { get; set; }
    public long AccountIdentifier { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public bool Hidden { get; set; }
  }
}