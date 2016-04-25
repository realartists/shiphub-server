namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;

  public class Authorization : GitHubModel {
    public string Token { get; set; }
    public IEnumerable<string> Scopes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
  }
}
