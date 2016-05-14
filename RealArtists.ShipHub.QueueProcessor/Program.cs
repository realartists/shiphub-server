namespace RealArtists.ShipHub.QueueProcessor {
  using Microsoft.Azure;
  using Microsoft.Azure.WebJobs;
  using Microsoft.Azure.WebJobs.ServiceBus;
  using QueueClient;

  static class Program {
    static void Main() {
      var config = new JobHostConfiguration();
      var sbConfig = new ServiceBusConfiguration();

      sbConfig.MessageOptions.MaxConcurrentCalls = 128;

      // TOOD: Override default messaging provider?
      //sbConfig.MessagingProvider = 

      // See https://github.com/Azure/azure-webjobs-sdk/wiki/Running-Locally
      if (config.IsDevelopment) {
        config.UseDevelopmentSettings();
        //config.DashboardConnectionString = null;
        //config.Tracing.Tracers.Clear();
        //config.Tracing.ConsoleLevel = TraceLevel.Error;
        sbConfig.MessageOptions.MaxConcurrentCalls = 1;
      }

      // https://azure.microsoft.com/en-us/documentation/articles/service-bus-performance-improvements/ recommends
      // 20x the processing rate/sec
      var ratePerSecond = 1;
      sbConfig.PrefetchCount = sbConfig.MessageOptions.MaxConcurrentCalls * 20 * ratePerSecond;

      config.UseServiceBus(sbConfig);

//#if DEBUG
//      ShipHubQueueClient.EnsureQueues().Wait();
//      var qc = new ShipHubQueueClient();
//      qc.SyncAccount(CloudConfigurationManager.GetSetting("Nick.Revoke.AuthToken")).Wait();
//#endif

      new JobHost(config).RunAndBlock();
    }
  }
}
