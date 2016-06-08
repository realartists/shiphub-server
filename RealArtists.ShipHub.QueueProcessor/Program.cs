namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Diagnostics;
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
        config.DashboardConnectionString = null;
        //config.Tracing.Tracers.Clear();
        //config.Tracing.ConsoleLevel = TraceLevel.Error;
        sbConfig.MessageOptions.MaxConcurrentCalls = 1;
      }

      // https://azure.microsoft.com/en-us/documentation/articles/service-bus-performance-improvements/ recommends
      // 20x the processing rate/sec
      var ratePerSecond = 1;
      sbConfig.PrefetchCount = sbConfig.MessageOptions.MaxConcurrentCalls * 20 * ratePerSecond;

      config.UseServiceBus(sbConfig);

#if DEBUG
      var timer = new Stopwatch();
      Console.WriteLine("Creating Missing Queues");
      timer.Start();
      ShipHubQueueClient.EnsureQueues().Wait();
      timer.Stop();
      Console.WriteLine($"Done in {timer.Elapsed}\n");

      Console.Write("Send Sync Message? [y/N]: ");
      var key = Console.Read();
      if (key == (int)'y') {
        Console.WriteLine("Sending sync account message");
        timer.Restart();
        var qc = new ShipHubQueueClient();
        qc.SyncAccount(CloudConfigurationManager.GetSetting("GitHubTestToken")).Wait();
        timer.Stop();
        Console.WriteLine($"Done in {timer.Elapsed}\n");
      }
#endif

      Console.WriteLine("Starting job host...\n\n");
      new JobHost(config).RunAndBlock();
    }
  }
}
