namespace RealArtists.ShipHub.Database.Deployment {
  using System;
  using System.Configuration;
  using System.Data.SqlClient;
  using System.Diagnostics;
  using Microsoft.SqlServer.Dac;

  public class Program {
    public static void Main(string[] args) {
      // Path shenanigans
      var path = Environment.GetEnvironmentVariable("PATH");
      path = $"{Environment.CurrentDirectory};{path}";
      Environment.SetEnvironmentVariable("PATH", path);

      Environment.SetEnvironmentVariable("SQLDBExtensionsRefPath", Environment.CurrentDirectory);
      Environment.SetEnvironmentVariable("SSDTPath", Environment.CurrentDirectory);

      // Does this work on Azure?
      var connectionString = ConfigurationManager.ConnectionStrings["ShipHubContext"].ConnectionString;
      var csBuilder = new SqlConnectionStringBuilder(connectionString);

      // This doesn't work yet. Hack aroung it :(

      //var dacServices = new DacServices(connectionString);
      //dacServices.Message += DacServices_Message;
      //dacServices.ProgressChanged += DacServices_ProgressChanged;

      //var profile = DacProfile.Load("RealArtists.ShipHub.Database.publish.xml");

      //var package = DacPackage.Load("RealArtists.ShipHub.Database.dacpac");

      //dacServices.Deploy(package, csBuilder.InitialCatalog, upgradeExisting: true, options: profile.DeployOptions);

      var startInfo = new ProcessStartInfo("sqlpackage.exe") {
        Arguments = $"/Action:Publish /SourceFile:RealArtists.ShipHub.Database.dacpac /Profile:RealArtists.ShipHub.Database.publish.xml /TargetConnectionString:\"{connectionString}\"",
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
      };
      var proc = new Process() { StartInfo = startInfo };
      proc.OutputDataReceived += Proc_OutputDataReceived;
      proc.ErrorDataReceived += Proc_ErrorDataReceived;

      proc.Start();
      proc.BeginOutputReadLine();
      proc.BeginErrorReadLine();

      proc.WaitForExit();

#if DEBUG
      Console.WriteLine("Done! Press a key to exit.");
      Console.ReadKey();
#endif
    }

    private static void Proc_ErrorDataReceived(object sender, DataReceivedEventArgs e) {
      Console.Error.WriteLine(e.Data);
    }

    private static void Proc_OutputDataReceived(object sender, DataReceivedEventArgs e) {
      Console.Out.WriteLine(e.Data);
    }

    private static void DacServices_ProgressChanged(object sender, DacProgressEventArgs e) {
      Console.WriteLine($"[{e.OperationId}:{e.Status}] {e.Message}");
    }

    private static void DacServices_Message(object sender, DacMessageEventArgs e) {
      Console.WriteLine(e.Message);
    }
  }
}
