﻿namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Microsoft.ApplicationInsights.Extensibility;
  using Microsoft.Azure;
  using Microsoft.Azure.WebJobs;
  using Microsoft.Azure.WebJobs.ServiceBus;
  using QueueClient;
  using SimpleInjector;

  static class Program {
    public const string ApplicationInsightsKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
    public const string RaygunApiKey = "RAYGUN_APIKEY";

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    static void Main() {
      var azureWebJobsDashboard = CloudConfigurationManager.GetSetting("AzureWebJobsDashboard");
      var azureWebJobsStorage = CloudConfigurationManager.GetSetting("AzureWebJobsStorage");

      var container = CreateContainer();

      // Job Host Configuration
      var config = new JobHostConfiguration() {
        DashboardConnectionString = azureWebJobsDashboard,
        StorageConnectionString = azureWebJobsStorage,
        JobActivator = new SimpleInjectorJobActivator(container),
      };
      config.Queues.MaxDequeueCount = 2; // Only try twice

      ConfigureLogging(config);

      var azureWebJobsServiceBus = CloudConfigurationManager.GetSetting("AzureWebJobsServiceBus");
      var sbConfig = new ServiceBusConfiguration() {
        ConnectionString = azureWebJobsServiceBus,
      };
      sbConfig.MessageOptions.MaxConcurrentCalls = 128;

#if DEBUG
      config.UseDevelopmentSettings();
      config.DashboardConnectionString = null;
      sbConfig.MessageOptions.AutoRenewTimeout = TimeSpan.FromSeconds(10); // Abandon locks quickly
      sbConfig.MessageOptions.MaxConcurrentCalls = 1;
      config.Queues.MaxDequeueCount = 1;
#endif

      // https://azure.microsoft.com/en-us/documentation/articles/service-bus-performance-improvements/ recommends
      // 20x the processing rate/sec
      var ratePerSecond = 1;
      sbConfig.PrefetchCount = sbConfig.MessageOptions.MaxConcurrentCalls * 20 * ratePerSecond;

#if DEBUG
      var timer = new Stopwatch();
      Console.WriteLine($"Initializing Service Bus.");
      timer.Start();
#endif

      var sbFactory = container.GetInstance<IServiceBusFactory>();

#if DEBUG
      timer.Stop();
      Console.WriteLine($"Done in {timer.Elapsed}\n");
#endif

      // Override default messaging provider to use pairing.
      sbConfig.MessagingProvider = new PairedMessagingProvider(sbConfig, sbFactory);

      config.UseServiceBus(sbConfig);
      config.UseTimers();

      Console.WriteLine("Starting job host...\n\n");
      using (var host = new JobHost(config)) {
#if DEBUG
        host.Start();
        Console.WriteLine("Press Any Key to Exit.");
        Console.ReadLine();
        Console.WriteLine("Stopping job host...");
        host.Stop();
#else
        host.RunAndBlock();
#endif
      }
    }

    static void ConfigureLogging(JobHostConfiguration config) {
      // Application Insights
      var instrumentationKey = CloudConfigurationManager.GetSetting(ApplicationInsightsKey);
      if (!instrumentationKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = instrumentationKey;
        config.Tracing.Tracers.Add(new ApplicationInsightsTraceWriter());
      }

      // Raygun
      var apiKey = CloudConfigurationManager.GetSetting(RaygunApiKey);
      if (!apiKey.IsNullOrWhiteSpace()) {
        config.Tracing.Tracers.Add(new RaygunTraceWriter(apiKey));
      }
    }

    static Container CreateContainer() {
      Container container = null;

      try {
        container = new Container();

        // AutoMapper
        container.Register(() => new MapperConfiguration(cfg => {
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
      } catch {
        if (container != null) {
          container.Dispose();
          throw;
        }
      }

      return container;
    }
  }
}
