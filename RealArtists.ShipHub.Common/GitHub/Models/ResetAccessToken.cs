namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System.Collections.Generic;

  public class ResetAccessToken {
    public long Id { get; set; }
    public IEnumerable<string> Scopes { get; set; }
    public string Token { get; set; }
    public Account User { get; set; }
  }
}
