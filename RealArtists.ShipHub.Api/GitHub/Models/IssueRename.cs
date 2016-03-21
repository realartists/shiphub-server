namespace RealArtists.ShipHub.Api.GitHub.Models {
  public class IssueRename : GitHubModel {
    public string From { get; set; }
    public string To { get; set; }
  }
}
