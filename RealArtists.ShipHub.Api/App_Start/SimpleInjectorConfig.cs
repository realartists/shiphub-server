namespace RealArtists.ShipHub.Api {
  using System.Diagnostics.CodeAnalysis;
  using System.Web.Http;
  using ActorInterfaces.Injection;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Mail;
  using Orleans;
  using QueueClient;
  using SimpleInjector;
  using SimpleInjector.Integration.WebApi;
  using Sync.Messages;

  public static class SimpleInjectorConfig {
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public static void Register(HttpConfiguration config) {
      var container = new Container();
      container.Options.DefaultScopedLifestyle = new WebApiRequestLifestyle();

      // ShipHubConfiguration
      container.Register(() => new ShipHubCloudConfiguration(), Lifestyle.Singleton);

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
      container.RegisterSingleton<IGrainFactory>(new LazyGrainFactory(() => {
        var orleansConfig = OrleansAzureClient.DefaultConfiguration();
        OrleansAzureClient.Initialize(orleansConfig);
        return GrainClient.GrainFactory;
      }));

      // Queue Client
      container.Register<IShipHubQueueClient, ShipHubQueueClient>(Lifestyle.Singleton);

      // Sync Manager
      container.Register<ISyncManager, SyncManager>(Lifestyle.Singleton);

      container.Register<IShipHubMailer, ShipHubMailer>(Lifestyle.Singleton);

      // This is an extension method from the integration package.
      container.RegisterWebApiControllers(GlobalConfiguration.Configuration);

      container.Verify();

      GlobalConfiguration.Configuration.DependencyResolver =
          new SimpleInjectorWebApiDependencyResolver(container);
    }
  }
}
