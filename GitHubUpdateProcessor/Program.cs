namespace GitHubUpdateProcessor {
  using Microsoft.Azure.WebJobs;
  using Microsoft.Azure.WebJobs.ServiceBus;

  static class Program {
    static void Main() {
      var config = new JobHostConfiguration();
      var sbConfig = new ServiceBusConfiguration();

      if (config.IsDevelopment) {
        config.UseDevelopmentSettings();
        sbConfig.MessageOptions.MaxConcurrentCalls = 1;
      }

      config.UseServiceBus(sbConfig);

      var host = new JobHost(config);
      host.RunAndBlock();
    }
  }
}
