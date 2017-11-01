namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;
  using Orleans.Serialization;

  // Allows us to make getting grain references async, so we can initialize the client if needed.
  public interface IAsyncGrainFactory {
    Task BindGrainReference(IAddressable grain);
    Task<TGrainObserverInterface> CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver;
    [SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
    Task DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver;
    Task<TGrainInterface> GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey;
    Task<TGrainInterface> GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey;
    Task<TGrainInterface> GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey;
    Task<TGrainInterface> GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey;
    Task<TGrainInterface> GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey;
  }

  public class OrleansAzureClient : IAsyncGrainFactory {
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60); // Cluster client call timeout.

    private const int MaxRetries = 120;  // 120 x 5s = Total: 10 minutes
    private static readonly TimeSpan StartupRetryPause = TimeSpan.FromSeconds(5); // Amount of time to pause before each retry attempt.

    /// <summary>
    /// Hack to ensure the OrleansAzureUtils assembly gets copied as referenced assembly.
    /// </summary>
    [Obsolete("Dirty hack.")]
    public static IEnumerable<DirectoryInfo> AppDirectoryLocations => Orleans.Runtime.Host.AzureConfigUtils.AppDirectoryLocations;

    public string DeploymentId { get; private set; }
    public string DataConnectionString { get; private set; }
    public ClientConfiguration Configuration { get; private set; }
    public Assembly ActorApplicationPart { get; private set; }

    private Lazy<Task<IClusterClient>> _instance;

    public OrleansAzureClient(string deploymentId, string dataConnectionString, Assembly actorApplicationPart) {
      DeploymentId = deploymentId;
      DataConnectionString = dataConnectionString;

      Configuration = new ClientConfiguration {
        GatewayProvider = ClientConfiguration.GatewayProviderType.AzureTable,
        DeploymentId = DeploymentId,
        DataConnectionString = DataConnectionString,
        ResponseTimeout = ResponseTimeout,
        FallbackSerializationProvider = typeof(ILBasedSerializer).GetTypeInfo(),
      };

      Configuration.SerializationProviders.Add(typeof(JsonObjectSerializer).GetTypeInfo());

      ActorApplicationPart = actorApplicationPart;

      _instance = new Lazy<Task<IClusterClient>>(new Func<Task<IClusterClient>>(CreateOrleansClient), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private async Task<IClusterClient> CreateOrleansClient() {
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
            .AddApplicationPart(ActorApplicationPart)
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

    public async Task BindGrainReference(IAddressable grain) {
      (await _instance.Value).BindGrainReference(grain);
    }

    public async Task<TGrainObserverInterface> CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver {
      return await (await _instance.Value).CreateObjectReference<TGrainObserverInterface>(obj);
    }

    public async Task DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver {
      await (await _instance.Value).DeleteObjectReference<TGrainObserverInterface>(obj);
    }

    public async Task<TGrainInterface> GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey {
      return (await _instance.Value).GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    }

    public async Task<TGrainInterface> GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey {
      return (await _instance.Value).GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    }

    public async Task<TGrainInterface> GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey {
      return (await _instance.Value).GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    }

    public async Task<TGrainInterface> GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey {
      return (await _instance.Value).GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    }

    public async Task<TGrainInterface> GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey {
      return (await _instance.Value).GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    }
  }
}
