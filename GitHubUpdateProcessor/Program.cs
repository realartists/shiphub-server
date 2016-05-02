namespace GitHubUpdateProcessor {
  using Microsoft.Azure.WebJobs;

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
