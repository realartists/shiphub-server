namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Microsoft.Extensions.DependencyInjection;
  using QueueClient;

  public class ShipStartupProvider {
    public IServiceProvider ConfigureServices(IServiceCollection services) {
      // Just use the Microsoft container.

      // Configuration
      services.AddSingleton<IShipHubConfiguration, ShipHubCloudConfiguration>();

      var connectionString = ShipHubCloudConfiguration.Instance.ShipHubContext;
      services.AddSingleton<IFactory<ShipHubContext>>(
        new GenericFactory<ShipHubContext>(() => new ShipHubContext(connectionString)));

      // AutoMapper
      services.AddSingleton(
        (_) => new MapperConfiguration(cfg => {
          cfg.AddProfile<GitHubToDataModelProfile>();
        }).CreateMapper());

      // Service Bus
      services.AddSingleton<IServiceBusFactory>((_) => {
        // HACK: This is gross
        var sbf = new ServiceBusFactory();
        sbf.Initialize().GetAwaiter().GetResult();
        return sbf;
      });

      // Queue Client
      services.AddSingleton<IShipHubQueueClient, ShipHubQueueClient>();

      return services.BuildServiceProvider();
    }
  }
}
