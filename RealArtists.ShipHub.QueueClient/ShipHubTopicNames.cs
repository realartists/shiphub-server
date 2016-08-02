namespace RealArtists.ShipHub.QueueClient {
  using System.Collections.Generic;
  using System.Linq;
  using System.Reflection;

  public static class ShipHubTopicNames {
    public const string DeadLetterSuffix = "/$DeadLetterQueue";

    public static IEnumerable<string> AllTopics { get; private set; }

    static ShipHubTopicNames() {
      AllTopics = typeof(ShipHubTopicNames)
         .GetFields(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Static)
         .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
         .Select(x => (string)x.GetRawConstantValue())
         .Where(x => x != DeadLetterSuffix)
         .ToArray();
    }

    // Topics
    public const string Changes = "changes";
  }
}
