namespace RealArtists.ShipHub.ActorDevSilo {
  using System;
  using ActorInterfaces.GitHub;
  using Orleans;
  using Orleans.Runtime.Configuration;
  using Common;

  public class Program {
    private static OrleansHostWrapper hostWrapper;

    static void Main(string[] args) {
      // The Orleans silo environment is initialized in its own app domain in order to more
      // closely emulate the distributed situation, when the client and the server cannot
      // pass data via shared memory.
      AppDomain hostDomain = AppDomain.CreateDomain("OrleansHost", null, new AppDomainSetup {
        AppDomainInitializer = InitSilo,
        AppDomainInitializerArguments = args,
      });

      var config = ClientConfiguration.LocalhostSilo();
      GrainClient.Initialize(config);

      Console.WriteLine("Orleans Silo is running.\nPress Enter to terminate...");
      Console.ReadLine();

      hostDomain.DoCallBack(ShutdownSilo);
    }

    static void InitSilo(string[] args) {
      hostWrapper = new OrleansHostWrapper(args);

      if (!hostWrapper.Run()) {
        Console.Error.WriteLine("Failed to initialize Orleans silo");
      }
    }

    static void ShutdownSilo() {
      if (hostWrapper != null) {
        hostWrapper.Dispose();
        GC.SuppressFinalize(hostWrapper);
      }
    }
  }
}
