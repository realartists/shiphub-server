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
    const string SyncPrefix = "sync";
    //const string UpdatePrefix = "update";

    // Resources
    const string Account = "-account";
    const string Comments = "-comments";
    const string Issues = "-issues";
    const string IssueEvents = "-issueEvents";
    const string IssueTimeline = "-issueTimeline";
    const string Milestones = "-milestones";
    const string Organization = "-organization";
    const string Organizations = "-organizations";
    //const string RateLimit = "-rateLimit";
    const string Repository = "-repository";
    const string Repositories = "-repositories";
    //const string Webhook = "-webhook";

    // Queues [Action + Resource]
    public const string SyncAccount = SyncPrefix + Account;
    public const string SyncAccountRepositories = SyncAccount + Repositories;
    public const string SyncAccountOrganizations = SyncAccount + Organizations;

    public const string SyncOrganizationMembers = SyncPrefix + Organization + "-members";

    public const string SyncRepository = SyncPrefix + Repository;
    public const string SyncRepositoryAssignees = SyncRepository + "-assignees";
    public const string SyncRepositoryIssues = SyncRepository + Issues;
    public const string SyncRepositoryLabels = SyncRepository + "-labels";
    public const string SyncRepositoryMilestones = SyncRepository + Milestones;
    public const string SyncRepositoryComments = SyncRepository + Comments;
    public const string SyncRepositoryIssueEvents = SyncRepository + IssueEvents;
    public const string SyncRepositoryIssueTimeline = SyncRepository + IssueTimeline;

    //public const string UpdateAccount = UpdatePrefix + Account;
    //public const string UpdateAccountRepositories = UpdateAccount + "-repositories";
    //public const string UpdateRepository = UpdatePrefix + Repository;
    //public const string UpdateRepositoryAssignable = UpdateRepository + "-assignable";
  }
}
