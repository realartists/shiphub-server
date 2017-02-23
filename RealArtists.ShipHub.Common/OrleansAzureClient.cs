namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Linq;
  using System.Threading;
  using Microsoft.Azure;
  using Orleans;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;

  /// <summary>
  /// Utility class for initializing an Orleans client running inside Azure App Services.
  /// Based off of https://github.com/dotnet/orleans/blob/master/src/OrleansAzureUtils/Hosting/AzureClient.cs
  /// </summary>
  public static class OrleansAzureClient {
    /// <summary>Number of retry attempts to make when searching for gateway silos to connect to.</summary>
    const int MaxRetries = 120;  // 120 x 5s = Total: 10 minutes
    const string DataConnectionSettingsKey = "DataConnectionString";
    const string DeploymentIdSettingsKey = "DeploymentId";
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Amount of time to pause before each retry attempt.</summary>
    static readonly TimeSpan StartupRetryPause = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Hack to ensure the OrleansAzureUtils assembly gets copied.
    /// </summary>
    [Obsolete("Dirty hack.")]
    public static IEnumerable<DirectoryInfo> AppDirectoryLocations { get { return Orleans.Runtime.Host.AzureConfigUtils.AppDirectoryLocations; } }

    /// <summary>
    /// Returns default client configuration object for passing to AzureClient.
    /// </summary>
    /// <returns></returns>
    public static ClientConfiguration DefaultConfiguration() {
      var config = new ClientConfiguration {
        GatewayProvider = ClientConfiguration.GatewayProviderType.AzureTable,
        DeploymentId = CloudConfigurationManager.GetSetting(DeploymentIdSettingsKey),
        DataConnectionString = CloudConfigurationManager.GetSetting(DataConnectionSettingsKey),
        TraceFilePattern = "false",
        TraceToConsole = false,
        ResponseTimeout = ResponseTimeout,
      };

      return config;
    }

    /// <summary>
    /// Initializes the Orleans client runtime in this Azure process from the provided client configuration object. 
    /// If the configuration object is null, the initialization fails. 
    /// </summary>
    /// <param name="config">A ClientConfiguration object.</param>
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "It's a retry loop.")]
    public static void Initialize(ClientConfiguration config) {
      if (GrainClient.IsInitialized) {
        return;
      }

      //// Find endpoint info for the gateway to this Orleans silo cluster
      //Trace.WriteLine("Searching for Orleans gateway silo via Orleans instance table...");
      var deploymentId = config.DeploymentId;
      var connectionString = config.DataConnectionString;
      if (deploymentId.IsNullOrWhiteSpace()) {
        throw new ConfigurationErrorsException($"Cannot connect to Azure silos with null deploymentId. config.DeploymentId = {config.DeploymentId}");
      }

      if (connectionString.IsNullOrWhiteSpace()) {
        throw new ConfigurationErrorsException($"Cannot connect to Azure silos with null connectionString. config.DataConnectionString = {config.DataConnectionString}");
      }

      // Work around https://github.com/dotnet/orleans/issues/2152
      var listeners = new TraceListener[Trace.Listeners.Count];
      Trace.Listeners.CopyTo(listeners, 0);
      Trace.Listeners.Clear();

      Log.Info($"Initializing Orleans Client with configuration:\n{config.SerializeObject(Newtonsoft.Json.Formatting.Indented)}");
      Exception lastException = null;
      for (int i = 0; i < MaxRetries; i++) {
        if (i > 0) {
          // Pause to let Primary silo start up and register
          Thread.Sleep(StartupRetryPause);
        }

        try {
          // Initialize will throw if cannot find Gateways
          GrainClient.Initialize(config);
          return;
        } catch (Exception exc) {
          lastException = exc;
          exc.Report("Error initializing Orleans Client. Will retry.");
        }
      }

      // Work around https://github.com/dotnet/orleans/issues/2152
      // Restore trace listeners
      Trace.Listeners.AddRange(listeners);

      if (lastException != null) {
        throw new OrleansException($"Could not Initialize Client for DeploymentId={deploymentId}.", lastException);
      } else {
        throw new OrleansException($"Could not Initialize Client for DeploymentId={deploymentId}.");
      }
    }
  }
}
