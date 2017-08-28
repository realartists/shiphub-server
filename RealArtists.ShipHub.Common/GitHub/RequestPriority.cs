namespace RealArtists.ShipHub.Common.GitHub {
  /// <summary>
  /// Used to indicate the importance of requests.
  /// </summary>
  public enum RequestPriority {
    /// <summary>
    /// Used to catch serialization failures.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The request is not urgent. All higher priority requests should be completed first.
    /// </summary>
    Background = 1,

    /// <summary>
    /// The request is required to complete another request that has already started.
    /// This allows pagination for background requests to complete before additional background requests.
    /// Interactive requests should still be competed before these.
    /// </summary>
    SubRequest = 2,

    /// <summary>
    /// The user is waiting on the result of this request.
    /// Pagination required by Interactive requests should also be marked interactive.
    /// Use judiciously, or background requests may be entirely starved.
    /// </summary>
    Interactive = 3,

    /// <summary>
    /// Ignore the number, because this is currently the lowest priority level.
    /// Only make the request if the user has ample remaining rate limit (> 2000) and is not currently occupied.
    /// </summary>
    PublicPool = 4,
  }
}
