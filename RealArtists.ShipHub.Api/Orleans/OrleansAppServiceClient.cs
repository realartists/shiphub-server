namespace RealArtists.ShipHub.Api.Orleans {
  using System;
  using System.Diagnostics;
  using System.IO;
  using System.Threading;
  using global::Orleans;
  using global::Orleans.Runtime;
  using global::Orleans.Runtime.Configuration;
  using Microsoft.Azure;

  /// <summary>
  /// Utility class for initializing an Orleans client running inside Azure App Services.
  /// Based off of https://github.com/dotnet/orleans/blob/master/src/OrleansAzureUtils/Hosting/AzureClient.cs
  /// </summary>
  public static class OrleansAppServiceClient {
    /// <summary>Number of retry attempts to make when searching for gateway silos to connect to.</summary>
    public static readonly int MaxRetries = AppServiceConstants.StartupMaxAttempts;  // 120 x 5s = Total: 10 minutes
                                                                         /// <summary>Amount of time to pause before each retry attempt.</summary>
    public static readonly TimeSpan StartupRetryPause = AppServiceConstants.StartupRetryDelay; // 5 seconds

    /// <summary>
    /// Whether the Orleans Azure client runtime has already been initialized
    /// </summary>
    /// <returns><c>true</c> if client runtime is already initialized</returns>
    public static bool IsInitialized { get { return GrainClient.IsInitialized; } }

    /// <summary>
    /// Initialise the Orleans client runtime in this Azure process
    /// </summary>
    public static void Initialize() {
      InitializeImpl_FromFile(null);
    }

    /// <summary>
    /// Initialise the Orleans client runtime in this Azure process
    /// </summary>
    /// <param name="orleansClientConfigFile">Location of the Orleans client config file to use for base config settings</param>
    /// <remarks>Any silo gateway address specified in the config file is ignored, and gateway endpoint info is read from the silo instance table in Azure storage instead.</remarks>
    public static void Initialize(FileInfo orleansClientConfigFile) {
      InitializeImpl_FromFile(orleansClientConfigFile);
    }

    /// <summary>
    /// Initialise the Orleans client runtime in this Azure process
    /// </summary>
    /// <param name="clientConfigFilePath">Location of the Orleans client config file to use for base config settings</param>
    /// <remarks>Any silo gateway address specified in the config file is ignored, and gateway endpoint info is read from the silo instance table in Azure storage instead.</remarks>
    public static void Initialize(string clientConfigFilePath) {
      InitializeImpl_FromFile(new FileInfo(clientConfigFilePath));
    }

    /// <summary>
    /// Initializes the Orleans client runtime in this Azure process from the provided client configuration object. 
    /// If the configuration object is null, the initialization fails. 
    /// </summary>
    /// <param name="config">A ClientConfiguration object.</param>
    public static void Initialize(ClientConfiguration config) {
      InitializeImpl_FromConfig(config);
    }

    /// <summary>
    /// Uninitializes the Orleans client runtime in this Azure process. 
    /// </summary>
    public static void Uninitialize() {
      if (!GrainClient.IsInitialized)
        return;

      Trace.TraceInformation("Uninitializing connection to Orleans gateway silo.");
      GrainClient.Uninitialize();
    }

    /// <summary>
    /// Returns default client configuration object for passing to AzureClient.
    /// </summary>
    /// <returns></returns>
    public static ClientConfiguration DefaultConfiguration() {
      var config = new ClientConfiguration {
        GatewayProvider = ClientConfiguration.GatewayProviderType.AzureTable,
        DeploymentId = GetDeploymentId(),
        DataConnectionString = GetDataConnectionString(),
      };

      return config;
    }

    #region Internal implementation of client initialization processing

    private static void InitializeImpl_FromFile(FileInfo configFile) {
      if (GrainClient.IsInitialized) {
        Trace.TraceInformation("Connection to Orleans gateway silo already initialized.");
        return;
      }

      ClientConfiguration config;
      try {
        if (configFile == null) {
          Trace.TraceInformation("Looking for standard Orleans client config file");
          config = ClientConfiguration.StandardLoad();
        } else {
          var configFileLocation = configFile.FullName;
          Trace.TraceInformation("Loading Orleans client config file {0}", configFileLocation);
          config = ClientConfiguration.LoadFromFile(configFileLocation);
        }
      } catch (Exception ex) {
        var msg = $"Error loading Orleans client configuration file {configFile} {ex.Message} -- unable to continue. {LogFormatter.PrintException(ex)}";
        Trace.TraceError(msg);
        throw new AggregateException(msg, ex);
      }

      Trace.TraceInformation("Overriding Orleans client config from App Service runtime environment.");
      try {
        config.DeploymentId = GetDeploymentId();
        config.DataConnectionString = GetDataConnectionString();
        config.GatewayProvider = ClientConfiguration.GatewayProviderType.AzureTable;
      } catch (Exception ex) {
        var msg = $"ERROR: No AzureClient role setting value '{AppServiceConstants.DataConnectionSettingsKey}' specified for this role -- unable to continue";
        Trace.TraceError(msg);
        throw new AggregateException(msg, ex);
      }

      InitializeImpl_FromConfig(config);
    }

    internal static string GetDeploymentId() {
      return CloudConfigurationManager.GetSetting(AppServiceConstants.DeploymentIdSettingsKey);
    }

    internal static string GetDataConnectionString() {
      return CloudConfigurationManager.GetSetting(AppServiceConstants.DataConnectionSettingsKey);
    }

    private static void InitializeImpl_FromConfig(ClientConfiguration config) {
      if (GrainClient.IsInitialized) {
        Trace.TraceInformation("Connection to Orleans gateway silo already initialized.");
        return;
      }

      //// Find endpoint info for the gateway to this Orleans silo cluster
      //Trace.WriteLine("Searching for Orleans gateway silo via Orleans instance table...");
      var deploymentId = config.DeploymentId;
      var connectionString = config.DataConnectionString;
      if (string.IsNullOrEmpty(deploymentId))
        throw new ArgumentException("Cannot connect to Azure silos with null deploymentId", "config.DeploymentId");

      if (string.IsNullOrEmpty(connectionString))
        throw new ArgumentException("Cannot connect to Azure silos with null connectionString", "config.DataConnectionString");

      bool initSucceeded = false;
      Exception lastException = null;
      for (int i = 0; i < MaxRetries; i++) {
        try {
          // Initialize will throw if cannot find Gateways
          GrainClient.Initialize(config);
          initSucceeded = true;
          break;
        } catch (Exception exc) {
          lastException = exc;
          Trace.TraceError("Client.Initialize failed with exc -- {0}. Will try again", exc.Message);
        }
        // Pause to let Primary silo start up and register
        Trace.TraceInformation("Pausing {0} awaiting silo and gateways registration for Deployment={1}", StartupRetryPause, deploymentId);
        Thread.Sleep(StartupRetryPause);
      }

      if (initSucceeded)
        return;

      OrleansException err;
      err = lastException != null ? new OrleansException($"Could not Initialize Client for DeploymentId={deploymentId}. Last exception={lastException.Message}",
        lastException) : new OrleansException($"Could not Initialize Client for DeploymentId={deploymentId}.");
      Trace.TraceError("Error starting Orleans Azure client application -- {0} -- bailing. {1}", err.Message, LogFormatter.PrintException(err));
      throw err;
    }

    #endregion
  }
}
