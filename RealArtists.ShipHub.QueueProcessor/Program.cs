namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Diagnostics;
  using Common;
  using Microsoft.ApplicationInsights.Extensibility;
  using Microsoft.Azure;
  using Microsoft.Azure.WebJobs;
  using Microsoft.Azure.WebJobs.ServiceBus;
  using QueueClient;

  static class Program {
    public const string ApplicationInsightsKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
    public const string RaygunApiKey = "RAYGUN_APIKEY";

    static void Main() {
      var azureWebJobsDashboard = CloudConfigurationManager.GetSetting("AzureWebJobsDashboard");
      var azureWebJobsStorage = CloudConfigurationManager.GetSetting("AzureWebJobsStorage");
      var config = new JobHostConfiguration() {
        DashboardConnectionString = azureWebJobsDashboard,
        StorageConnectionString = azureWebJobsStorage,
      };
      config.Queues.MaxDequeueCount = 2; // Only try twice

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

      var azureWebJobsServiceBus = CloudConfigurationManager.GetSetting("AzureWebJobsServiceBus");
      var sbConfig = new ServiceBusConfiguration() {
        ConnectionString = azureWebJobsServiceBus,
      };
      sbConfig.MessageOptions.MaxConcurrentCalls = 128;

      // Adjust this based on real performance data
      //sbConfig.MessageOptions.AutoRenewTimeout = 

      // See https://github.com/Azure/azure-webjobs-sdk/wiki/Running-Locally
      if (config.IsDevelopment) {
        config.UseDevelopmentSettings();
        config.DashboardConnectionString = null;
        sbConfig.MessageOptions.AutoRenewTimeout = TimeSpan.FromSeconds(10); // Abandon locks quickly
        sbConfig.MessageOptions.MaxConcurrentCalls = 1;
        config.Queues.MaxDequeueCount = 1;
      }

      // https://azure.microsoft.com/en-us/documentation/articles/service-bus-performance-improvements/ recommends
      // 20x the processing rate/sec
      var ratePerSecond = 1;
      sbConfig.PrefetchCount = sbConfig.MessageOptions.MaxConcurrentCalls * 20 * ratePerSecond;

#if DEBUG
      var timer = new Stopwatch();
      Console.WriteLine($"Initializing Service Bus.");
      timer.Start();
#endif

      var sbFactory = new ServiceBusFactory(); // Defaults are fine.
      sbFactory.Initialize().Wait();

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
  }
}
