namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
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

      Exception lastException = null;
      for (int i = 0; i < MaxRetries; i++) {
        try {
          // Initialize will throw if cannot find Gateways
          GrainClient.Initialize(config);
          return;
        } catch (Exception exc) {
          lastException = exc;
        }

        // Pause to let Primary silo start up and register
        Thread.Sleep(StartupRetryPause);
      }

      OrleansException err;
      err = lastException != null ? new OrleansException($"Could not Initialize Client for DeploymentId={deploymentId}. Last exception={lastException.Message}",
        lastException) : new OrleansException($"Could not Initialize Client for DeploymentId={deploymentId}.");
      throw err;
    }
  }
}
