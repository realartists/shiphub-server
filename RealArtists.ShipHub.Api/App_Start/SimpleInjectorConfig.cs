namespace RealArtists.ShipHub.Api {
  using System.Diagnostics.CodeAnalysis;
  using System.Web.Http;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Mail;
  using Mixpanel;
  using QueueClient;
  using RealArtists.ChargeBee;
  using RealArtists.ShipHub.ActorInterfaces;
  using SimpleInjector;
  using SimpleInjector.Integration.WebApi;
  using SimpleInjector.Lifestyles;
  using Sync.Messages;

  public static class SimpleInjectorConfig {
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public static void Register(IShipHubConfiguration config) {
      var container = new Container();
      container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();

      // ShipHubConfiguration
      container.RegisterSingleton(config);

      // AutoMapper
      container.RegisterSingleton(() => new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
        cfg.AddProfile<DataModelToApiModelProfile>();
      }).CreateMapper());

      // Service Bus
      container.RegisterSingleton<IServiceBusFactory>(() => {
        // HACK: This is gross
        var sbf = new ServiceBusFactory();
        sbf.Initialize().GetAwaiter().GetResult();
        return sbf;
      });

      // Orleans
      var actorAssembly = typeof(IEchoActor).Assembly;
      container.RegisterSingleton<IAsyncGrainFactory>(new OrleansAzureClient(config.DeploymentId, config.DataConnectionString, actorAssembly));

      // Queue Client
      container.RegisterSingleton<IShipHubQueueClient, ShipHubQueueClient>();

      // Sync Manager
      container.RegisterSingleton<ISyncManager, SyncManager>();

      // Mailer
      container.RegisterSingleton<IShipHubMailer, ShipHubMailer>();

      // ChargeBee
      if (!config.ChargeBeeHostAndKey.IsNullOrWhiteSpace()) {
        var parts = config.ChargeBeeHostAndKey.Split(':');
        container.RegisterSingleton(() => new ChargeBeeApi(parts[0], parts[1]));
      }

      // Mixpanel (maybe not safe as a singleton?)
      container.Register<IMixpanelClient>(() => new MixpanelClient(config.MixpanelToken, new MixpanelConfig() {
        ErrorLogFn = (message, exception) => {
          Log.Exception(exception, message);
        },
        IpAddressHandling = MixpanelIpAddressHandling.IgnoreRequestIp,
      }));

      // This is an extension method from the integration package.
      container.RegisterWebApiControllers(GlobalConfiguration.Configuration);

      container.Verify();

      GlobalConfiguration.Configuration.DependencyResolver =
          new SimpleInjectorWebApiDependencyResolver(container);
    }
  }
}
