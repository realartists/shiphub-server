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
      container.Register(() => new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
        cfg.AddProfile<DataModelToApiModelProfile>();
      }).CreateMapper(),
        Lifestyle.Singleton);

      // Service Bus
      container.Register<IServiceBusFactory>(() => {
        // HACK: This is gross
        var sbf = new ServiceBusFactory();
        sbf.Initialize().GetAwaiter().GetResult();
        return sbf;
      }, Lifestyle.Singleton);

      // Orleans
      container.RegisterSingleton<IAsyncGrainFactory>(new OrleansAzureClient(config.DeploymentId, config.DataConnectionString));

      // Queue Client
      container.Register<IShipHubQueueClient, ShipHubQueueClient>(Lifestyle.Singleton);

      // Sync Manager
      container.Register<ISyncManager, SyncManager>(Lifestyle.Singleton);

      // Mailer
      container.Register<IShipHubMailer>(() => new ShipHubMailer(), Lifestyle.Singleton);

      // ChargeBee
      if (!config.ChargeBeeHostAndKey.IsNullOrWhiteSpace()) {
        var parts = config.ChargeBeeHostAndKey.Split(':');
        container.Register(() => new ChargeBeeApi(parts[0], parts[1]), Lifestyle.Singleton);
      }

      // Mixpanel
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
