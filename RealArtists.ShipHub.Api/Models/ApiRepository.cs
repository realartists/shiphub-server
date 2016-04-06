namespace RealArtists.ShipHub.Api.Models {
  public class ApiRepository {
    public int Identifier { get; set; }
    public int AccountIdentifier { get; set; }
    public bool Private { get; set; }
    public bool HasIssues { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public string RepoDescription { get; set; }
    public bool Hidden { get; set; }
    public long? RowVersion { get; set; }
  }
}