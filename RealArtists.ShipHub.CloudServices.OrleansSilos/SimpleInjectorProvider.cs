namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Microsoft.Azure;
  using Microsoft.Extensions.DependencyInjection;
  using QueueClient;
  using SimpleInjector;

  public class SimpleInjectorProvider {
    private class FallThroughProvider : IServiceProvider {
      private IEnumerable<IServiceProvider> _providers;

      public FallThroughProvider(params IServiceProvider[] providers) {
        _providers = providers;
      }

      public object GetService(Type serviceType) {
        foreach (var provider in _providers) {
          var service = provider.GetService(serviceType);
          if (service != null) {
            return service;
          }
        }
        return null;
      }
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public IServiceProvider ConfigureServices(IServiceCollection services) {
      Log.Trace();

      var container = new Container();

      // Configuration
      container.Register<IShipHubConfiguration, ShipHubCloudConfiguration>(Lifestyle.Singleton);

      var connectionString = ShipHubCloudConfiguration.Instance.ShipHubContext;
      container.RegisterSingleton<IFactory<ShipHubContext>>(
        new GenericFactory<ShipHubContext>(() => new ShipHubContext(connectionString)));

      // AutoMapper
      container.Register(
        () => new MapperConfiguration(cfg => {
          cfg.AddProfile<GitHubToDataModelProfile>();
        }).CreateMapper(),
        Lifestyle.Singleton);

      // Service Bus
      container.Register<IServiceBusFactory>(() => {
        // HACK: This is gross
        var sbf = new ServiceBusFactory();
        sbf.Initialize().GetAwaiter().GetResult();
        return sbf;
      }, Lifestyle.Singleton);

      // Queue Client
      container.Register<IShipHubQueueClient, ShipHubQueueClient>(Lifestyle.Singleton);

      container.Verify();

      return new FallThroughProvider(container, services.BuildServiceProvider());
    }
  }
}
