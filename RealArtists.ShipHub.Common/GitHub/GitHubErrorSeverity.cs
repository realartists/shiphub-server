namespace RealArtists.ShipHub.Common.GitHub {
  /// <summary>
  /// How to respond to various GitHubErrors
  /// </summary>
  public enum GitHubErrorSeverity {
    /// <summary>
    /// Uninitialzied value. You found a bug.
    /// </summary>
    NotSpecified = 0,

    /// <summary>
    /// Please retry the request.
    /// </summary>
    Retry,

    /// <summary>
    /// The request has permanently failed and will never succeed. Ex: token revoked
    /// </summary>
    Failed,

    /// <summary>
    /// The request has failed and future requests should not be attempted. Ex: abuse rate limited
    /// </summary>
    Abuse,

    /// <summary>
    /// The request was rate limited. Retry after limit resets, or use a different token.
    /// </summary>
    RateLimited,
  }
}
