namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System.Diagnostics;
  using System.Linq;
  using System.Net;
  using System.Threading;
  using System.Threading.Tasks;
  using Microsoft.Azure;
  using Microsoft.WindowsAzure.ServiceRuntime;
  using Orleans.Runtime.Configuration;
  using Orleans.Runtime.Host;

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
      runCompleteEvent.WaitOne();

      if (_silo != null) {
        _silo.Stop();
        _silo = null;
      }

      RoleEnvironment.Changing -= RoleEnvironmentChanging;

      base.OnStop();
    }

    private async Task RunAsync(CancellationToken cancellationToken) {
      while (!cancellationToken.IsCancellationRequested) {
        Trace.TraceInformation("Working");

        var config = AzureSilo.DefaultConfiguration();

        // This allows App Services and Cloud Services to agree on a deploymentId.
        config.Globals.DeploymentId = CloudConfigurationManager.GetSetting("DeploymentId");

        config.AddMemoryStorageProvider();
        config.AddAzureTableStorageProvider("AzureStore", CloudConfigurationManager.GetSetting("DataConnectionString"));

        // It is IMPORTANT to start the silo not in OnStart but in Run.
        // Azure may not have the firewalls open yet (on the remote silos) at the OnStart phase.
        _silo = new AzureSilo();
        bool ok = _silo.Start(config);

        // Block until silo is shutdown
        _silo.Run();
      }
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
