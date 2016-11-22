namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
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
    private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

    private AzureSilo _silo;
    private CancellationTokenSource _cancellationTokenSource;

    public override bool OnStart() {
      Log.Trace();

      LogTraceListener.Configure();

      var aiKey = ShipHubCloudConfiguration.Instance.ApplicationInsightsKey;
      if (!aiKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = aiKey;
        LogManager.TelemetryConsumers.Add(new Orleans.TelemetryConsumers.AI.AITelemetryConsumer());
      }

      var raygunKey = ShipHubCloudConfiguration.Instance.RaygunApiKey;
      if (!raygunKey.IsNullOrWhiteSpace()) {
        LogManager.TelemetryConsumers.Add(new RaygunTelemetryConsumer(raygunKey));
      }

      // Set the maximum number of concurrent connections
      ServicePointManager.DefaultConnectionLimit = 12;

      // For information on handling configuration changes
      // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.
      RoleEnvironment.Changing += RoleEnvironmentChanging;

      return base.OnStart();
    }

    public override void Run() {
      Log.Trace();

      try {
        using (_cancellationTokenSource = new CancellationTokenSource()) {
          Log.Info("Running worker.");
          runCompleteEvent.Reset();
          RunAsync(_cancellationTokenSource.Token).Wait();
          Log.Info("Worker exited.");
        }
      } catch (Exception e) {
        Log.Exception(e);
        throw;
      } finally {
        runCompleteEvent.Set();
        _cancellationTokenSource = null;
      }
    }

    public override void OnStop() {
      Log.Trace();

      _cancellationTokenSource?.Cancel();

      var silo = _silo;
      _silo = null;

      if (silo != null) {
        Log.Info("Stopping silo.");
        silo.Stop();

        Log.Info("Waiting for worker to stop.");
        runCompleteEvent.WaitOne();
        Log.Info("Worker stopped.");
      } else {
        Log.Info("Silo is null. Wat?");
      }

      RoleEnvironment.Changing -= RoleEnvironmentChanging;

      base.OnStop();
    }

    private Task RunAsync(CancellationToken cancellationToken) {
      while (!cancellationToken.IsCancellationRequested) {
        var shipHubConfig = new ShipHubCloudConfiguration();
        var siloConfig = AzureSilo.DefaultConfiguration();

        // This allows App Services and Cloud Services to agree on a deploymentId.
        siloConfig.Globals.DeploymentId = shipHubConfig.DeploymentId;

        // Dependency Injection
        siloConfig.UseStartupType<SimpleInjectorProvider>();

        siloConfig.AddMemoryStorageProvider();
        siloConfig.AddAzureTableStorageProvider("AzureStore", shipHubConfig.DataConnectionString);

        // It is IMPORTANT to start the silo not in OnStart but in Run.
        // Azure may not have the firewalls open yet (on the remote silos) at the OnStart phase.
        var silo = new AzureSilo();
        silo.Start(siloConfig);
        _silo = silo;

        // Block until silo is shutdown
        silo.Run();
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
