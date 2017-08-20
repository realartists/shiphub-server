namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using System.Collections.Generic;

  public class PullRequestReviewConnection {
    public PageInfo PageInfo { get; set; }

    public IEnumerable<PullRequestReview> Nodes { get; set; }
  }
}
