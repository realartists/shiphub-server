namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Reflection;
  using Common;
  using Microsoft.ApplicationInsights.Extensibility;
  using Microsoft.WindowsAzure.ServiceRuntime;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;
  using Orleans.Runtime.Host;
  using Orleans.Serialization;
  using Orleans.TelemetryConsumers.AI;

  // For lifecycle details see https://docs.microsoft.com/en-us/azure/cloud-services/cloud-services-role-lifecycle-dotnet
  [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
  public class WorkerRole : RoleEntryPoint {
    private static readonly TimeSpan SiloRequestTimeout = TimeSpan.FromMinutes(30);

    private AzureSilo _silo;
    private ShipHubCloudConfiguration _config = new ShipHubCloudConfiguration();

    public override bool OnStart() {
      Log.Trace();

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointDefaultConnectionLimit();

      LogTraceListener.Configure();

      if (!_config.ApplicationInsightsKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = _config.ApplicationInsightsKey;
        LogManager.TelemetryConsumers.Add(new AITelemetryConsumer(_config.ApplicationInsightsKey));
      }

      if (!_config.RaygunApiKey.IsNullOrWhiteSpace()) {
        LogManager.TelemetryConsumers.Add(new RaygunTelemetryConsumer(_config.RaygunApiKey));
      }

      //if (!_config.StatHatKey.IsNullOrWhiteSpace()) {
      //  LogManager.TelemetryConsumers.Add(new StatHatTelemetryConsumer());
      //}

      // For information on handling configuration changes
      // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.
      RoleEnvironment.Changing += RoleEnvironmentChanging;
      AppDomain.CurrentDomain.UnhandledException +=
        (object sender, UnhandledExceptionEventArgs e) => {
          if (e.ExceptionObject is Exception ex) {
            ex.Report("Unhandled exception in Orleans CloudServices Worker Role.");
          }
        };

      return base.OnStart();
    }

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public override void Run() {
      Log.Trace();

      try {
        var siloConfig = AzureSilo.DefaultConfiguration();

        // Silo timeout is substantially longer than client timeout to allow sync to wait.
        siloConfig.Globals.ResponseTimeout = SiloRequestTimeout;

        // This allows App Services and Cloud Services to agree on a deploymentId.
        siloConfig.Globals.DeploymentId = _config.DeploymentId;

        // Add custom JSON.Net object serialization
        siloConfig.Globals.SerializationProviders.Add(typeof(JsonObjectSerializer).GetTypeInfo());

        // Ensure exceptions can be serialized
        siloConfig.Globals.FallbackSerializationProvider = typeof(ILBasedSerializer).GetTypeInfo();

        // Dependency Injection
        siloConfig.UseStartupType<ShipStartupProvider>();

        siloConfig.AddAzureTableStorageProvider("AzureStore", _config.DataConnectionString);

        // It is IMPORTANT to start the silo not in OnStart but in Run.
        // Azure may not have the firewalls open yet (on the remote silos) at the OnStart phase.
        _silo = new AzureSilo();
        _silo.Start(siloConfig);

        // Block until silo is shutdown
        _silo.Run();
      } catch (Exception e) {
        e.Report("Error while running silo. Aborting.");
      }

      Log.Info("Run loop exiting.");
    }

    public override void OnStop() {
      Log.Trace();

      if (_silo != null) {
        Log.Info("Stopping silo.");
        _silo.Stop();
        Log.Info("Stopped silo.");
        _silo = null;
      }

      base.OnStop();
    }

    private static void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e) {
      var i = 1;
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
