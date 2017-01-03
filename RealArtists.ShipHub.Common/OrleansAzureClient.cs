namespace RealArtists.ShipHub.Common {
  using System;
  using System.Configuration;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Reflection;
  using System.Threading;
  using Microsoft.Azure;
  using Orleans;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;
  using Orleans.Runtime.Host;
  using Orleans.Serialization;

  /// <summary>
  /// Utility class for initializing an Orleans client running inside Azure App Services.
  /// Based off of https://github.com/dotnet/orleans/blob/master/src/OrleansAzureUtils/Hosting/AzureClient.cs
  /// </summary>
  public static class OrleansAzureClient {
    /// <summary>Number of retry attempts to make when searching for gateway silos to connect to.</summary>
    public const int MaxRetries = 120;  // 120 x 5s = Total: 10 minutes

    /// <summary>Amount of time to pause before each retry attempt.</summary>
    public static readonly TimeSpan StartupRetryPause = TimeSpan.FromSeconds(5);

    public const string DataConnectionSettingsKey = "DataConnectionString";
    public const string DeploymentIdSettingsKey = "DeploymentId";

    // HACK: Ensure required DLLs get detected as dependencies and copied
    [Obsolete("Don't use this.")]
    public static readonly bool AzureClientReference = AzureClient.IsInitialized;

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
      if (!GrainClient.IsInitialized) {
        return;
      }

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
        FallbackSerializationProvider = typeof(ILBasedSerializer).GetTypeInfo(),
      };

      return config;
    }

    private static void InitializeImpl_FromFile(FileInfo configFile) {
      if (GrainClient.IsInitialized) {
        return;
      }

      ClientConfiguration config;
      try {
        if (configFile == null) {
          config = ClientConfiguration.StandardLoad();
        } else {
          var configFileLocation = configFile.FullName;
          config = ClientConfiguration.LoadFromFile(configFileLocation);
        }
      } catch (Exception ex) {
        var msg = $"Error loading Orleans client configuration file {configFile} {ex.Message} -- unable to continue. {LogFormatter.PrintException(ex)}";
        throw new AggregateException(msg, ex);
      }

      try {
        config.DeploymentId = GetDeploymentId();
        config.DataConnectionString = GetDataConnectionString();
        config.GatewayProvider = ClientConfiguration.GatewayProviderType.AzureTable;
      } catch (Exception ex) {
        var msg = $"ERROR: No AzureClient role setting value '{DataConnectionSettingsKey}' specified for this role -- unable to continue";
        throw new AggregateException(msg, ex);
      }

      InitializeImpl_FromConfig(config);
    }

    internal static string GetDeploymentId() {
      return CloudConfigurationManager.GetSetting(DeploymentIdSettingsKey);
    }

    internal static string GetDataConnectionString() {
      return CloudConfigurationManager.GetSetting(DataConnectionSettingsKey);
    }

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "It's a retry loop.")]
    private static void InitializeImpl_FromConfig(ClientConfiguration config) {
      if (GrainClient.IsInitialized) {
        return;
      }

      //// Find endpoint info for the gateway to this Orleans silo cluster
      //Trace.WriteLine("Searching for Orleans gateway silo via Orleans instance table...");
      var deploymentId = config.DeploymentId;
      var connectionString = config.DataConnectionString;
      if (string.IsNullOrEmpty(deploymentId)) {
        throw new ConfigurationErrorsException($"Cannot connect to Azure silos with null deploymentId. config.DeploymentId = {config.DeploymentId}");
      }

      if (string.IsNullOrEmpty(connectionString)) {
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
