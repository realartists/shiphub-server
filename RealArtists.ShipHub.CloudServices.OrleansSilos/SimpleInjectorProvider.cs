namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Collections.Generic;
  using Common;
  using Common.DataModel;
  using Microsoft.Azure;
  using Microsoft.Extensions.DependencyInjection;
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

      container.Register<IFactory<ShipHubContext>>(() => {
        var connectionString = CloudConfigurationManager.GetSetting("ShipHubContext");
        return new GenericFactory<ShipHubContext>(() => new ShipHubContext(connectionString));
      }, Lifestyle.Singleton);

      container.Verify();

      return new FallThroughProvider(container, services.BuildServiceProvider());
    }
  }
}
