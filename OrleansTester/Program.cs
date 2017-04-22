namespace OrleansTester {
  using System;
  using System.Threading.Tasks;
  using Orleans;
  using RealArtists.ShipHub.ActorInterfaces;
  using RealArtists.ShipHub.Common;

  class Program {
    static void Main(string[] args) {
      DoIt().Wait();
      Console.WriteLine("Done");
      Console.ReadKey();
    }

    static async Task DoIt() {
      var orleansConfig = OrleansAzureClient.DefaultConfiguration();
      OrleansAzureClient.Initialize(orleansConfig);
      var gc = GrainClient.GrainFactory;

      var user = gc.GetGrain<IUserActor>(87309); // kogir

      var repo = gc.GetGrain<IRepositoryActor>(59613425); // realartists/test

      var issue = gc.GetGrain<IIssueActor>(139, "realartists/test", grainClassNamePrefix: null); // realartists/test#139

      Console.WriteLine("[q]: Quit, [u]: Sync user [r] Sync repo [i] Sync issue");
      ConsoleKeyInfo keyInfo;
      while ((keyInfo = Console.ReadKey(true)).Key != ConsoleKey.Q) {
        switch (keyInfo.Key) {
          case ConsoleKey.U:
            Console.WriteLine($"[{DateTimeOffset.Now}]: Syncing user {user.GetPrimaryKeyLong()}.");
            await user.Sync();
            break;
          case ConsoleKey.R:
            Console.WriteLine($"[{DateTimeOffset.Now}]: Syncing repository {repo.GetPrimaryKeyLong()}.");
            await repo.Sync();
            break;
          case ConsoleKey.I:
            Console.WriteLine($"[{DateTimeOffset.Now}]: Syncing issue {issue.GetPrimaryKeyLong(out string repoName)} in {repoName}.");
            await issue.SyncInteractive(user.GetPrimaryKeyLong());
            break;
        }
      }
    }
  }
}
