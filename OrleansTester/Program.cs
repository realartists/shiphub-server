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

      //var repo = gc.GetGrain<IRepositoryActor>(51336290);
      //var repo = gc.GetGrain<IRepositoryActor>(46592358);
      var user = gc.GetGrain<IUserActor>(87309);

      Console.WriteLine("[Q]: Quit, [Any Key]: Sync again.");
      do {
        //Console.WriteLine($"[{DateTimeOffset.Now}]: Syncing repository {repo.GetPrimaryKeyLong()}.");
        //await repo.Sync();
        Console.WriteLine($"[{DateTimeOffset.Now}]: Syncing user {user.GetPrimaryKeyLong()}.");
        await user.Sync();
      } while (Console.ReadKey().Key != ConsoleKey.Q);
    }
  }
}
