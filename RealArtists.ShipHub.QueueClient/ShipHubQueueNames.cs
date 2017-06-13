namespace RealArtists.ShipHub.QueueClient {
  using System.Collections.Generic;
  using System.Linq;
  using System.Reflection;

  public static class ShipHubQueueNames {
    public const string DeadLetterSuffix = "/$DeadLetterQueue";

    public static IEnumerable<string> AllQueues { get; private set; }

    static ShipHubQueueNames() {
      AllQueues = typeof(ShipHubQueueNames)
         .GetFields(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Static)
         .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
         .Select(x => (string)x.GetRawConstantValue())
         .Where(x => x != DeadLetterSuffix)
         .ToArray();
    }

    // Queue Names
    public const string BillingGetOrCreatePersonalSubscription = "billing-get-or-create-personal-subscription";
    public const string BillingSyncOrgSubscriptionState = "billing-sync-org-subscription-state";
    public const string BillingUpdateComplimentarySubscription = "billing-update-complimentary-subscription";
  }
}
