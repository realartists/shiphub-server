namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Collections.Generic;
  using Api.Sync.Messages;
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

    public IServiceProvider ConfigureServices(IServiceCollection services) {
      var container = new Container();

      var connectionString = CloudConfigurationManager.GetSetting("ShipHubContext");
      container.RegisterSingleton<IFactory<ShipHubContext>>(
        new GenericFactory<ShipHubContext>(() => new ShipHubContext(connectionString)));

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

      // Queue Client
      container.Register<IShipHubQueueClient, ShipHubQueueClient>(Lifestyle.Singleton);

      container.Verify();

      return new FallThroughProvider(container, services.BuildServiceProvider());
    }
  }
}
