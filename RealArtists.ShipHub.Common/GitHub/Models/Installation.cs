namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class Installation {
    public long Id { get; set; }
    public Account Account { get; set; }
    public string RepositorySelection { get; set; }
  }
}
