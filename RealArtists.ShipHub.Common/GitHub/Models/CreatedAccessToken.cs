namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class CreatedAccessToken : GitHubModel {
    public string AccessToken { get; set; }
    public string Scope { get; set; }
    public string TokenType { get; set; }
  }
}
