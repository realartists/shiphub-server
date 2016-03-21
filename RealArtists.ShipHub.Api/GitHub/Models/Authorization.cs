namespace RealArtists.ShipHub.Api.GitHub.Models {
  using System.Collections.Generic;

  public class Authorization : GitHubModel {
    public IEnumerable<string> Scopes { get; set; }
    public string Token { get; set; }
  }
}
