namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Runtime.Serialization;

  [Serializable]
  public class GitHubRateException : Exception {
    public GitHubRateException() {
    }

    public GitHubRateException(string message) : base(message) {
    }

    public GitHubRateException(string message, Exception innerException) : base(message, innerException) {
    }

    public GitHubRateException(string userInfo, Uri uri, GitHubRateLimit limit, bool isAbuse = false)
      : base($"Throttling {userInfo}. {(isAbuse ? "[ABUSE]" : string.Empty)} [{limit.Remaining}/{limit.Limit} until {limit.Reset:o}] for {uri}") {
      IsAbuse = isAbuse;
      RateLimit = limit.Limit;
      RateLimitRemaining = limit.Remaining;
      RateLimitReset = limit.Reset;
    }

    protected GitHubRateException(SerializationInfo info, StreamingContext context) : base(info, context) {
      if (info != null) {
        IsAbuse = info.GetBoolean(nameof(IsAbuse));
        RateLimit = info.GetInt32(nameof(RateLimit));
        RateLimitRemaining = info.GetInt32(nameof(RateLimitRemaining));
        RateLimitReset = info.GetDateTime(nameof(RateLimitReset));
      }
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context) {
      base.GetObjectData(info, context);
      info.AddValue(nameof(IsAbuse), IsAbuse);
      info.AddValue(nameof(RateLimit), RateLimit);
      info.AddValue(nameof(RateLimitRemaining), RateLimitRemaining);
      info.AddValue(nameof(RateLimitReset), RateLimitReset);
    }

    public bool IsAbuse { get; set; }
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }
  }
}
