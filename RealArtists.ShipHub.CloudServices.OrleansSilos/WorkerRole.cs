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
  using Orleans.TelemetryConsumers.AI;

  // For lifecycle details see https://docs.microsoft.com/en-us/azure/cloud-services/cloud-services-role-lifecycle-dotnet
  [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
  public class WorkerRole : RoleEntryPoint {
    private bool _abort;
    private AzureSilo _silo;
    private ShipHubCloudConfiguration _config = new ShipHubCloudConfiguration();

    public override bool OnStart() {
      Log.Trace();

      LogTraceListener.Configure();

      if (!_config.ApplicationInsightsKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = _config.ApplicationInsightsKey;
        LogManager.TelemetryConsumers.Add(new AITelemetryConsumer(_config.ApplicationInsightsKey));
      }

      if (!_config.RaygunApiKey.IsNullOrWhiteSpace()) {
        LogManager.TelemetryConsumers.Add(new RaygunTelemetryConsumer(_config.RaygunApiKey));
      }

      // Set the maximum number of concurrent connections
      ServicePointManager.DefaultConnectionLimit = 4096;
      var github = ServicePointManager.FindServicePoint(new Uri("https://api.github.com"));
      github.ConnectionLimit = 32768;

      // For information on handling configuration changes
      // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.
      RoleEnvironment.Changing += RoleEnvironmentChanging;
      AppDomain.CurrentDomain.UnhandledException +=
        (object sender, UnhandledExceptionEventArgs e) => {
          var ex = e.ExceptionObject as Exception;
          if (ex != null) {
            Log.Exception(ex, "Unhandled exception in Orleans CloudServices Worker Role.");
          }
        };

      return base.OnStart();
    }

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public override void Run() {
      Log.Trace();

      while (!_abort) {
        try {
          var siloConfig = AzureSilo.DefaultConfiguration();

          // This allows App Services and Cloud Services to agree on a deploymentId.
          siloConfig.Globals.DeploymentId = _config.DeploymentId;

          // Dependency Injection
          siloConfig.UseStartupType<SimpleInjectorProvider>();

          siloConfig.AddMemoryStorageProvider();
          siloConfig.AddAzureTableStorageProvider("AzureStore", _config.DataConnectionString);

          // It is IMPORTANT to start the silo not in OnStart but in Run.
          // Azure may not have the firewalls open yet (on the remote silos) at the OnStart phase.
          _silo = new AzureSilo();
          _silo.Start(siloConfig);

          // Block until silo is shutdown
          _silo.Run();
        } catch (Exception e) {
          Log.Exception(e, "Error while running silo. Restarting within Run method.");
        }
      }
      Log.Info("Run loop exiting.");
    }

    public override void OnStop() {
      Log.Trace();

      _abort = true;
      if (_silo != null) {
        Log.Info("Stopping silo.");
        _silo.Stop();
        Log.Info("Stopped silo.");
      }

      base.OnStop();
    }

    private static void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e) {
      int i = 1;
      foreach (var c in e.Changes) {
        Log.Info(string.Format("RoleEnvironmentChanging: #{0} Type={1} Change={2}", i++, c.GetType().FullName, c));
      }

      // If a configuration setting is changing);
      if (e.Changes.Any((RoleEnvironmentChange change) => change is RoleEnvironmentConfigurationSettingChange)) {
        // Set e.Cancel to true to restart this role instance
        e.Cancel = true;
      }
    }
  }
}
