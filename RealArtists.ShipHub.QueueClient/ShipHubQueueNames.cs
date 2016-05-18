namespace RealArtists.ShipHub.QueueClient {
  using System.Collections.Generic;
  using System.Linq;
  using System.Reflection;

  public static class ShipHubQueueNames {
    public const string DeadLetterSuffix = "/$DeadLetterQueue";

    public static readonly IEnumerable<string> AllQueues;

    static ShipHubQueueNames() {
      AllQueues = typeof(ShipHubQueueNames)
         .GetFields(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Static)
         .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
         .Select(x => (string)x.GetRawConstantValue())
         .Where(x => x != DeadLetterSuffix)
         .ToArray();
    }
    // Actions
    const string SyncPrefix = "sync-";
    const string UpdatePrefix = "update-";

    // Resources
    const string Account = "account";
    const string Comment = "comment";
    const string Issue = "issue";
    const string IssueEvent = "issueEvent";
    const string Milestone = "milestone";
    const string Organization = "organization";
    const string RateLimit = "rateLimit";
    const string Repository = "repository";
    const string Webhook = "webhook";

    // Queues [Action + Resource]
    public const string SyncAccount = SyncPrefix + Account;
    public const string SyncAccountRepositories = SyncAccount + "-repositories";
    public const string SyncAccountOrganizations = SyncAccount + "-organizations";

    public const string SyncOrganizationMembers = SyncPrefix + Organization + "-members";

    public const string SyncRepository = SyncPrefix + Repository;
    public const string SyncRepositoryAssignees = SyncRepository + "-assignees";
    public const string SyncRepositoryIssues = SyncRepository + "-issues";
    public const string SyncRepositoryLabels = SyncRepository + "-labels";
    public const string SyncRepositoryMilestones = SyncRepository + "-milestones";

    //public const string UpdateAccount = UpdatePrefix + Account;
    //public const string UpdateAccountRepositories = UpdateAccount + "-repositories";
    //public const string UpdateRepository = UpdatePrefix + Repository;
    //public const string UpdateRepositoryAssignable = UpdateRepository + "-assignable";
  }
}
