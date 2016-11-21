namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Net;
  using System.Threading;
  using System.Threading.Tasks;
  using Common;
  using Microsoft.ApplicationInsights.Extensibility;
  using Microsoft.Azure;
  using Microsoft.WindowsAzure.ServiceRuntime;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;
  using Orleans.Runtime.Host;

  [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
  public class WorkerRole : RoleEntryPoint {
    private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

    private AzureSilo _silo;

    public override bool OnStart() {
      // Set the maximum number of concurrent connections
      ServicePointManager.DefaultConnectionLimit = 12;

      // For information on handling configuration changes
      // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.
      RoleEnvironment.Changing += RoleEnvironmentChanging;

      LogTraceListener.Configure();

      return base.OnStart();
    }

    public override void Run() {
      try {
        RunAsync(cancellationTokenSource.Token).Wait();
      } finally {
        runCompleteEvent.Set();
      }
    }

    public override void OnStop() {
      cancellationTokenSource.Cancel();

      if (_silo != null) {
        _silo.Stop();
        _silo = null;
      }

      runCompleteEvent.WaitOne();

      RoleEnvironment.Changing -= RoleEnvironmentChanging;

      base.OnStop();
    }

    private Task RunAsync(CancellationToken cancellationToken) {
      while (!cancellationToken.IsCancellationRequested) {
        Trace.TraceInformation("Working");

        var config = AzureSilo.DefaultConfiguration();

        var shipHubConfig = new ShipHubCloudConfiguration();

        // This allows App Services and Cloud Services to agree on a deploymentId.
        config.Globals.DeploymentId = shipHubConfig.DeploymentId;

        // Logging
        // Common.Log already configured (once) in OnStart
        var aiKey = ShipHubCloudConfiguration.Instance.ApplicationInsightsKey;
        if (!aiKey.IsNullOrWhiteSpace()) {
          TelemetryConfiguration.Active.InstrumentationKey = aiKey;
          LogManager.TelemetryConsumers.Add(new Orleans.TelemetryConsumers.AI.AITelemetryConsumer());
        }

        var raygunKey = ShipHubCloudConfiguration.Instance.RaygunApiKey;
        if (!raygunKey.IsNullOrWhiteSpace()) {
          LogManager.TelemetryConsumers.Add(new RaygunTelemetryConsumer(raygunKey));
        }

        // Dependency Injection
        config.UseStartupType<SimpleInjectorProvider>();

        config.AddMemoryStorageProvider();
        config.AddAzureTableStorageProvider("AzureStore", shipHubConfig.DataConnectionString);

        // It is IMPORTANT to start the silo not in OnStart but in Run.
        // Azure may not have the firewalls open yet (on the remote silos) at the OnStart phase.
        _silo = new AzureSilo();
        _silo.Start(config);

        // Block until silo is shutdown
        _silo.Run();
      }
      return Task.CompletedTask;
    }

    private static void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e) {
      int i = 1;
      foreach (var c in e.Changes) {
        Trace.WriteLine(string.Format("RoleEnvironmentChanging: #{0} Type={1} Change={2}", i++, c.GetType().FullName, c));
      }

      // If a configuration setting is changing);
      if (e.Changes.Any((RoleEnvironmentChange change) => change is RoleEnvironmentConfigurationSettingChange)) {
        // Set e.Cancel to true to restart this role instance
        e.Cancel = true;
      }
    }
  }
}
