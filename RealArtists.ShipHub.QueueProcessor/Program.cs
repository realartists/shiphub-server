namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Microsoft.ApplicationInsights;
  using Microsoft.ApplicationInsights.Extensibility;
  using Microsoft.Azure.WebJobs;
  using Microsoft.Azure.WebJobs.ServiceBus;
  using Mindscape.Raygun4Net;
  using QueueClient;
  using SimpleInjector;
  using Tracing;
  using cb = ChargeBee;

  static class Program {
    public const string ApplicationInsightsKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
    public const string RaygunApiKey = "RAYGUN_APIKEY";

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    static void Main() {
      Log.Trace();
      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointDefaultConnectionLimit();

      var shipHubConfig = new ShipHubCloudConfiguration();
      var azureWebJobsDashboard = shipHubConfig.AzureWebJobsDashboard;
      var azureWebJobsStorage = shipHubConfig.AzureWebJobsStorage;

      // Raygun Client
      var raygunApiKey = shipHubConfig.RaygunApiKey;
      RaygunClient raygunClient = null;
      if (!raygunApiKey.IsNullOrWhiteSpace()) {
        raygunClient = new RaygunClient(raygunApiKey);
        raygunClient.AddWrapperExceptions(typeof(AggregateException));
      }

      // App Insights Client
      var applicationInsightsKey = shipHubConfig.ApplicationInsightsKey;
      TelemetryClient telemetryClient = null;
      if (!applicationInsightsKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = applicationInsightsKey;
        telemetryClient = new TelemetryClient();
      }

      var detailedLogger = new DetailedExceptionLogger(telemetryClient, raygunClient);

      var container = CreateContainer(detailedLogger);

      // Hack to address https://github.com/realartists/shiphub-server/issues/277
      // I suspect that when DI occurs, the AzureBlobTraceListener is registered as a TraceListener
      // but not initialized. To get around this, force Orleans to initialize now, before any
      // TraceListeners get added.
      var timer = new Stopwatch();
      timer.Restart();
      Log.Info("[Orleans Client]: Initializing");
      container.GetInstance<IAsyncGrainFactory>();
      timer.Stop();
      Log.Info($"[Orleans Client]: Initialized in {timer.Elapsed}");

      // Job Host Configuration
      var config = new JobHostConfiguration() {
        DashboardConnectionString = azureWebJobsDashboard,
        StorageConnectionString = azureWebJobsStorage,
        JobActivator = new SimpleInjectorJobActivator(container),
      };
      config.Queues.MaxDequeueCount = 2; // Only try twice

      // Gross manual DI
      ConfigureGlobalLogging(config, telemetryClient, raygunClient);

      var azureWebJobsServiceBus = shipHubConfig.AzureWebJobsServiceBus;
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

      Log.Info($"[Service Bus]: Initializing");
      timer.Restart();
      var sbFactory = container.GetInstance<IServiceBusFactory>();
      timer.Stop();
      Log.Info($"[Service Bus]: Initialized in {timer.Elapsed}");

      // Override default messaging provider to use pairing.
      sbConfig.MessagingProvider = new PairedMessagingProvider(sbConfig, sbFactory);

      config.UseServiceBus(sbConfig);
      config.UseTimers();
      config.UseCore(); // For ExecutionContext

      Log.Info("[Job Host]: Starting");
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
        Log.Info("[Job Host]: Stopped");
      }
    }

    static void ConfigureGlobalLogging(JobHostConfiguration config, TelemetryClient telemetryClient, RaygunClient raygunClient) {
      // These are still needed to capture top level exceptions, but new injected logger should
      // provide much more useful information from functions that use it.

      // Application Insights
      if (telemetryClient != null) {
        config.Tracing.Tracers.Add(new ApplicationInsightsTraceWriter(telemetryClient));
      }

      // Raygun
      if (raygunClient != null) {
        config.Tracing.Tracers.Add(new RaygunTraceWriter(raygunClient));
      }
    }

    static Container CreateContainer(IDetailedExceptionLogger detailedLogger) {
      Container container = null;

      try {
        container = new Container();

        // ShipHub Configuration
        var config = ShipHubCloudConfiguration.Instance;
        container.RegisterSingleton(config);

        // AutoMapper
        container.RegisterSingleton(() => {
          var mapperConfig = new MapperConfiguration(cfg => {
            cfg.AddProfile<GitHubToDataModelProfile>();
          });
          return mapperConfig.CreateMapper();
        });

        // Service Bus
        container.RegisterSingleton<IServiceBusFactory>(() => {
          // HACK: This is gross
          var sbf = new ServiceBusFactory();
          sbf.Initialize().GetAwaiter().GetResult();
          return sbf;
        });

        // Orleans
        container.RegisterSingleton<IAsyncGrainFactory>(() => {
          var factory = new OrleansAzureClient(config.DeploymentId, config.DataConnectionString);
          factory.Configuration.DefaultTraceLevel = Orleans.Runtime.Severity.Error;
          return factory;
        });

        // Queue Client
        container.RegisterSingleton<IShipHubQueueClient, ShipHubQueueClient>();

        // IDetailedExceptionLogger
        container.RegisterSingleton(() => detailedLogger);

        // ChargeBee
        var chargeBeeHostAndApiKey = ShipHubCloudConfiguration.Instance.ChargeBeeHostAndKey;
        if (!chargeBeeHostAndApiKey.IsNullOrWhiteSpace()) {
          var parts = chargeBeeHostAndApiKey.Split(':');
          container.RegisterSingleton(() => new cb.ChargeBeeApi(parts[0], parts[1]));
        }

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
