namespace OrleansTestSite.Orleans {
  using System;

  /// <summary>
  /// Based on https://github.com/dotnet/orleans/blob/master/src/OrleansAzureUtils/Hosting/AzureConstants.cs
  /// Maybe better to extract values from AzureConstants using reflection?
  /// </summary>
  public static class AppServiceConstants {
    public static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(5); // In seconds
    public const int StartupMaxAttempts = 120;  // 120 x 5s = Total: 10 minutes
    public const string DeploymentIdSettingsKey = "DeploymentId";
    public const string DataConnectionSettingsKey = "DataConnectionString";
  }
}
