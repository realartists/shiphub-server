namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Diagnostics.CodeAnalysis;
  using Microsoft.Azure;

  /// <summary>
  /// Let's get rid of all the strings and formalize the required configuration values.
  /// </summary>
  public interface IShipHubConfiguration {
    string ApiHostName { get; }
    string ApplicationInsightsKey { get; }
    string AzureWebJobsDashboard { get; }
    string AzureWebJobsServiceBus { get; }
    string AzureWebJobsServiceBusPair { get; }
    string AzureWebJobsStorage { get; }
    string ChargeBeeHostAndKey { get; }
    string ChargeBeeWebhookSecret { get; }
    ISet<string> ChargeBeeWebhookIncludeOnlyList { get; }
    ISet<string> ChargeBeeWebhookExcludeList { get; }
    string DataConnectionString { get; }
    string DeploymentId { get; }
    string GitHubLoggingStorage { get; }
    string RaygunApiKey { get; }
    string ShipHubContext { get; }
    string SmtpPassword { get; }
    bool UseFiddler { get; }
  }

  /// <summary>
  /// For use during testing.
  /// </summary>
  public class ShipHubConfiguration : IShipHubConfiguration {
    public string ApiHostName { get; set; }
    public string ApplicationInsightsKey { get; set; }
    public string AzureWebJobsDashboard { get; set; }
    public string AzureWebJobsServiceBus { get; set; }
    public string AzureWebJobsServiceBusPair { get; set; }
    public string AzureWebJobsStorage { get; set; }
    public string ChargeBeeHostAndKey { get; set; }
    public string ChargeBeeWebhookSecret { get; set; }
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Only used in tests.")]
    public ISet<string> ChargeBeeWebhookIncludeOnlyList { get; set; }
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Only used in tests.")]
    public ISet<string> ChargeBeeWebhookExcludeList { get; set; }
    public string DataConnectionString { get; set; }
    public string DeploymentId { get; set; }
    public string GitHubLoggingStorage { get; set; }
    public string RaygunApiKey { get; set; }
    public string ShipHubContext { get; set; }
    public string SmtpPassword { get; set; }
    public bool UseFiddler { get; set; }
  }

  /// <summary>
  /// For use at runtime. Lazily grabs the values from the environment.
  /// </summary>
  public class ShipHubCloudConfiguration : IShipHubConfiguration {
    /// <summary>
    /// This is only for cases where it's a lot of trouble or really gross to inject. Breaks testability.
    /// </summary>
    public static IShipHubConfiguration Instance { get; } = new ShipHubCloudConfiguration();

    private Lazy<string> _apiHostName = new Lazy<string>(() => GetSetting("ApiHostName", required: true));
    public string ApiHostName { get { return _apiHostName.Value; } }

    private Lazy<string> _applicationInsightsKey = new Lazy<string>(() => GetSetting("APPINSIGHTS_INSTRUMENTATIONKEY"));
    public string ApplicationInsightsKey { get { return _applicationInsightsKey.Value; } }

    private Lazy<string> _azureWebJobsDashboard = new Lazy<string>(() => GetSetting("AzureWebJobsDashboard"));
    public string AzureWebJobsDashboard { get { return _azureWebJobsDashboard.Value; } }

    private Lazy<string> _azureWebJobsServiceBus = new Lazy<string>(() => GetSetting("AzureWebJobsServiceBus"));
    public string AzureWebJobsServiceBus { get { return _azureWebJobsServiceBus.Value; } }

    private Lazy<string> _azureWebJobsServiceBusPair = new Lazy<string>(() => GetSetting("AzureWebJobsServiceBusPair"));
    public string AzureWebJobsServiceBusPair { get { return _azureWebJobsServiceBusPair.Value; } }

    private Lazy<string> _azureWebJobsStorage = new Lazy<string>(() => GetSetting("AzureWebJobsStorage"));
    public string AzureWebJobsStorage { get { return _azureWebJobsStorage.Value; } }

    private Lazy<string> _chargeBeeHostAndKey = new Lazy<string>(() => GetSetting("ChargeBeeHostAndKey", required: true));
    public string ChargeBeeHostAndKey { get { return _chargeBeeHostAndKey.Value; } }

    private Lazy<string> _chargeBeeWebhookSecret = new Lazy<string>(() => GetSetting("ChargeBeeWebhookSecret", required: true));
    public string ChargeBeeWebhookSecret { get { return _chargeBeeWebhookSecret.Value; } }

    private Lazy<ISet<string>> _chargeBeeWebhookIncludeOnlyList = new Lazy<ISet<string>>(() => {
      var list = GetSetting("ChargeBeeWebhookIncludeOnlyList");
      if (!string.IsNullOrWhiteSpace(list)) {
        return list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
      }
      return null;
    });
    public ISet<string> ChargeBeeWebhookIncludeOnlyList { get { return _chargeBeeWebhookIncludeOnlyList.Value; } }

    private Lazy<ISet<string>> _chargeBeeWebhookExcludeList = new Lazy<ISet<string>>(() => {
      var list = GetSetting("ChargeBeeWebhookExcludeList");
      if (!string.IsNullOrWhiteSpace(list)) {
        return list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
      }
      return null;
    });
    public ISet<string> ChargeBeeWebhookExcludeList { get { return _chargeBeeWebhookExcludeList.Value; } }

    private Lazy<string> _dataConnectionString = new Lazy<string>(() => GetSetting("DataConnectionString", required: true));
    public string DataConnectionString { get { return _dataConnectionString.Value; } }

    private Lazy<string> _deploymentId = new Lazy<string>(() => GetSetting("DeploymentId", required: true));
    public string DeploymentId { get { return _deploymentId.Value; } }

    private Lazy<string> _gitHubLoggingStorage = new Lazy<string>(() => GetSetting("GitHubLoggingStorage"));
    public string GitHubLoggingStorage { get { return _gitHubLoggingStorage.Value; } }

    private Lazy<string> _raygunApiKey = new Lazy<string>(() => GetSetting("RAYGUN_APIKEY"));
    public string RaygunApiKey { get { return _raygunApiKey.Value; } }

    private Lazy<string> _shipHubContext = new Lazy<string>(() => GetSetting("ShipHubContext", required: true));
    public string ShipHubContext { get { return _shipHubContext.Value; } }

    private Lazy<string> _smtpPassword = new Lazy<string>(() => GetSetting("SmtpPassword", required: true));
    public string SmtpPassword { get { return _smtpPassword.Value; } }

    private Lazy<bool> _useFiddler = new Lazy<bool>(() => {
      bool result;
      return bool.TryParse(GetSetting("UseFiddler"), out result) && result;
    });
    public bool UseFiddler { get { return _useFiddler.Value; } }

    private static string GetSetting(string key, bool required = false) {
      string value = CloudConfigurationManager.GetSetting(key);
      if (value == null && required) {
        throw new ConfigurationErrorsException($"'{key}' is required but not specified in configuration.");
      }
      return value;
    }
  }
}
