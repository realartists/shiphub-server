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

    // Queue Names
    public const string SyncAccount = "sync-account";
    public const string SyncAccountRepositories = SyncAccount + "-repositories";
    public const string SyncAccountOrganizations = SyncAccount + "-organizations";

    public const string SyncOrganizationMembers = "sync-organization-members";

    public const string SyncRepository = "sync-repository";
    public const string SyncRepositoryAssignees = SyncRepository + "-assignees";
    public const string SyncRepositoryComments = SyncRepository + "-comments";
    public const string SyncRepositoryIssueComments = SyncRepository + "-issue-comments";
    public const string SyncRepositoryIssueEvents = SyncRepository + "-issue-events";
    public const string SyncRepositoryIssueTimeline = SyncRepository + "-issue-timeline";
    public const string SyncRepositoryIssues = SyncRepository + "-issues";
    public const string SyncRepositoryLabels = SyncRepository + "-labels";
    public const string SyncRepositoryMilestones = SyncRepository + "-milestones";
  }
}
