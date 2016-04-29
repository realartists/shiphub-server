namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  public static class ResourceQueueNames {
    public const string _Prefix = "ru-";

    public const string Account = _Prefix + "account";
    public const string Comment = _Prefix + "comment";
    public const string Issue = _Prefix + "issue";
    public const string IssueEvent = _Prefix + "issue-event";
    public const string Milestone = _Prefix + "milestone";
    public const string Repository = _Prefix + "repository";
    public const string Webhook = _Prefix + "webhook";
    public const string RateLimit = _Prefix + "rate-limit";
  }
}
