namespace GitHubUpdateProcessor {
  using Microsoft.Azure.WebJobs;
  using Microsoft.Azure.WebJobs.ServiceBus;
  static class Program {
    static void Main() {
      var config = new JobHostConfiguration();

      config.UseServiceBus();

      if (config.IsDevelopment) {
        config.UseDevelopmentSettings();
      }

      var host = new JobHost(config);
      host.RunAndBlock();
    }
  }
}
