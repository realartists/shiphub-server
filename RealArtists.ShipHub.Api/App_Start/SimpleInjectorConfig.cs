namespace RealArtists.ShipHub.Api {
  using System.Web.Http;
  using AutoMapper;
  using Common.DataModel;
  using QueueClient;
  using SimpleInjector;
  using SimpleInjector.Diagnostics;
  using SimpleInjector.Integration.WebApi;
  using Sync.Messages;

  public static class SimpleInjectorConfig {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public static void Register(HttpConfiguration config) {
      var container = new Container();
      container.Options.DefaultScopedLifestyle = new WebApiRequestLifestyle();

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
        sbf.Initialize().Wait();
        return sbf;
      }, Lifestyle.Singleton);

      // Queue Client
      container.Register<IShipHubQueueClient, ShipHubQueueClient>(Lifestyle.Singleton);

      // Sync Manager
      container.Register<ISyncManager, SyncManager>(Lifestyle.Singleton);

      // Database
      container.Register(() => new ShipHubContext(), Lifestyle.Transient);
      
      // This is an extension method from the integration package.
      container.RegisterWebApiControllers(GlobalConfiguration.Configuration);

      // Pre-verification hacks
      container
        .GetRegistration(typeof(ShipHubContext))
        .Registration
        .SuppressDiagnosticWarning(DiagnosticType.DisposableTransientComponent, "I know better.");

      container.Verify();

      GlobalConfiguration.Configuration.DependencyResolver =
          new SimpleInjectorWebApiDependencyResolver(container);
    }
  }
}
