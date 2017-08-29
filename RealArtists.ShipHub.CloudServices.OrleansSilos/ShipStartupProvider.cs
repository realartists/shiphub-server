namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Microsoft.Extensions.DependencyInjection;
  using Orleans;
  using QueueClient;
  using RealArtists.ShipHub.Actors;

  public class ShipStartupProvider {
    public IServiceProvider ConfigureServices(IServiceCollection services) {
      Log.Trace();

      // Just use the Microsoft container.

      var config = ShipHubCloudConfiguration.Instance;
      // Configuration
      services.AddSingleton(config);

      // Transient configuration
      services.AddSingleton<IShipHubRuntimeConfiguration, ShipHubRuntimeConfiguration>();

      services.AddSingleton<IFactory<ShipHubContext>>(
        new GenericFactory<ShipHubContext>(() => new ShipHubContext(config.ShipHubContext)));

      // AutoMapper
      services.AddSingleton(
        new MapperConfiguration(cfg => {
          cfg.AddProfile<GitHubToDataModelProfile>();
        }).CreateMapper());

      // Service Bus
      Log.Info($"Creating {nameof(ServiceBusFactory)}");
      // HACK: This is gross
      var sbf = new ServiceBusFactory();
      sbf.Initialize().GetAwaiter().GetResult();
      services.AddSingleton<IServiceBusFactory>(sbf);
      Log.Info($"Created {nameof(ServiceBusFactory)}");

      // Queue Client
      services.AddSingleton<IShipHubQueueClient, ShipHubQueueClient>();

      // TimeLoggerFilter Interceptor
      services.AddSingleton<IGrainCallFilter, TimeLoggerFilter>();

      return services.BuildServiceProvider();
    }
  }
}
