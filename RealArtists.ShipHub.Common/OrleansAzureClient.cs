namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.IO;
  using System.Reflection;
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;
  using Orleans.Serialization;

  public class OrleansClientFactory {
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60); // Cluser client call timeout.

    private const int MaxRetries = 120;  // 120 x 5s = Total: 10 minutes
    private static readonly TimeSpan StartupRetryPause = TimeSpan.FromSeconds(5); // Amount of time to pause before each retry attempt.

    /// <summary>
    /// Hack to ensure the OrleansAzureUtils assembly gets copied.
    /// </summary>
    [Obsolete("Dirty hack.")]
    public static IEnumerable<DirectoryInfo> AppDirectoryLocations => Orleans.Runtime.Host.AzureConfigUtils.AppDirectoryLocations;

    public string DeploymentId { get; private set; }
    public string DataConnectionString { get; private set; }
    public ClientConfiguration Configuration { get; private set; }

    public OrleansClientFactory(string deploymentId, string dataConnectionString) {
      DeploymentId = deploymentId;
      DataConnectionString = dataConnectionString;

      Configuration = new ClientConfiguration {
        GatewayProvider = ClientConfiguration.GatewayProviderType.AzureTable,
        DeploymentId = DeploymentId,
        DataConnectionString = DataConnectionString,
        TraceFilePattern = "false",
        TraceToConsole = false,
        ResponseTimeout = ResponseTimeout,
        FallbackSerializationProvider = typeof(ILBasedSerializer).GetTypeInfo(),
      };
    }

    public async Task<IClusterClient> CreateOrleansClient() {
      if (DeploymentId.IsNullOrWhiteSpace()) {
        throw new ConfigurationErrorsException($"Cannot connect to Azure silos with null deploymentId.");
      }

      if (DataConnectionString.IsNullOrWhiteSpace()) {
        throw new ConfigurationErrorsException($"Cannot connect to Azure silos with null connectionString.");
      }

      Log.Info($"Initializing Orleans Client.");
      Exception lastException = null;
      for (var i = 0; i < MaxRetries; i++) {
        if (i > 0) {
          // Pause to let Primary silo start up and register
          await Task.Delay(StartupRetryPause);
        }

        try {
          var client = new ClientBuilder()
            .UseConfiguration(Configuration)
            .AddClusterConnectionLostHandler((sender, e) => Log.Info("Orleans cluster connection lost."))
            .Build();
          // Connect will throw if cannot find Gateways
          await client.Connect();
          return client;
        } catch (Exception exc) {
          lastException = exc;
          exc.Report("Error initializing Orleans Client. Will retry.");
        }
      }

      if (lastException != null) {
        throw new OrleansException($"Could not Initialize Client for DeploymentId={DeploymentId}.", lastException);
      } else {
        throw new OrleansException($"Could not Initialize Client for DeploymentId={DeploymentId}.");
      }
    }
  }
}
