﻿namespace RealArtists.ShipHub.Common {
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
    string AzureWebJobsServiceBus { get; }
    string AzureWebJobsServiceBusPair { get; }
    string ChargeBeeHostAndKey { get; }
    string ChargeBeeWebhookSecret { get; }
    ISet<string> ChargeBeeWebhookIncludeOnlyList { get; }
    ISet<string> ChargeBeeWebhookExcludeList { get; }
    string DataConnectionString { get; }
    string DeploymentId { get; }
    Uri GitHubApiRoot { get; }
    string GitHubClientId { get; }
    string GitHubClientSecret { get; }
    string GitHubLoggingStorage { get; }
    string MixpanelToken { get; }
    string RaygunApiKey { get; }
    string ShipHubContext { get; }
    string SmtpPassword { get; }
    string StatHatKey { get; }
    string StatHatPrefix { get; }
    bool UseFiddler { get; }
    string WebsiteHostName { get; }
    string AdminSecret { get; }
    string AppleDeveloperMerchantIdDomainAssociation { get; }
  }

  /// <summary>
  /// For use during testing.
  /// </summary>
  public class ShipHubConfiguration : IShipHubConfiguration {
    public string ApiHostName { get; set; }
    public string ApplicationInsightsKey { get; set; }
    public string AzureWebJobsServiceBus { get; set; }
    public string AzureWebJobsServiceBusPair { get; set; }
    public string ChargeBeeHostAndKey { get; set; }
    public string ChargeBeeWebhookSecret { get; set; }
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Only used in tests.")]
    public ISet<string> ChargeBeeWebhookIncludeOnlyList { get; set; }
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Only used in tests.")]
    public ISet<string> ChargeBeeWebhookExcludeList { get; set; }
    public string DataConnectionString { get; set; }
    public string DeploymentId { get; set; }
    public Uri GitHubApiRoot { get; set; }
    public string GitHubClientId { get; set; }
    public string GitHubClientSecret { get; set; }
    public string GitHubLoggingStorage { get; set; }
    public string MixpanelToken { get; set; }
    public string RaygunApiKey { get; set; }
    public string ShipHubContext { get; set; }
    public string SmtpPassword { get; set; }
    public string StatHatKey { get; set; }
    public string StatHatPrefix { get; set; }
    public bool UseFiddler { get; set; }
    public string WebsiteHostName { get; set; }
    public string AdminSecret { get; set; }
    public string AppleDeveloperMerchantIdDomainAssociation { get; set; }
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
    public string ApiHostName => _apiHostName.Value;

    private Lazy<string> _applicationInsightsKey = new Lazy<string>(() => GetSetting("APPINSIGHTS_INSTRUMENTATIONKEY"));
    public string ApplicationInsightsKey => _applicationInsightsKey.Value;

    private Lazy<string> _azureWebJobsServiceBus = new Lazy<string>(() => GetSetting("AzureWebJobsServiceBus"));
    public string AzureWebJobsServiceBus => _azureWebJobsServiceBus.Value;

    private Lazy<string> _azureWebJobsServiceBusPair = new Lazy<string>(() => GetSetting("AzureWebJobsServiceBusPair"));
    public string AzureWebJobsServiceBusPair => _azureWebJobsServiceBusPair.Value;

    private Lazy<string> _chargeBeeHostAndKey = new Lazy<string>(() => GetSetting("ChargeBeeHostAndKey", required: true));
    public string ChargeBeeHostAndKey => _chargeBeeHostAndKey.Value;

    private Lazy<string> _chargeBeeWebhookSecret = new Lazy<string>(() => GetSetting("ChargeBeeWebhookSecret", required: true));
    public string ChargeBeeWebhookSecret => _chargeBeeWebhookSecret.Value;

    private Lazy<ISet<string>> _chargeBeeWebhookIncludeOnlyList = new Lazy<ISet<string>>(() => {
      var list = GetSetting("ChargeBeeWebhookIncludeOnlyList");
      if (!string.IsNullOrWhiteSpace(list)) {
        return list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
      }
      return null;
    });
    public ISet<string> ChargeBeeWebhookIncludeOnlyList => _chargeBeeWebhookIncludeOnlyList.Value;

    private Lazy<ISet<string>> _chargeBeeWebhookExcludeList = new Lazy<ISet<string>>(() => {
      var list = GetSetting("ChargeBeeWebhookExcludeList");
      if (!string.IsNullOrWhiteSpace(list)) {
        return list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
      }
      return null;
    });
    public ISet<string> ChargeBeeWebhookExcludeList => _chargeBeeWebhookExcludeList.Value;

    private Lazy<string> _dataConnectionString = new Lazy<string>(() => GetSetting("DataConnectionString", required: true));
    public string DataConnectionString => _dataConnectionString.Value;

    private Lazy<string> _deploymentId = new Lazy<string>(() => GetSetting("DeploymentId", required: true));
    public string DeploymentId => _deploymentId.Value;

    private Lazy<Uri> _gitHubApiRoot = new Lazy<Uri>(() => {
      var root = GetSetting("GitHubApiRoot");
      if (root.IsNullOrWhiteSpace()) {
        return new Uri("https://api.github.com/");
      } else {
        var apiRoot = new Uri(root);
        if (!apiRoot.IsAbsoluteUri
        || !apiRoot.AbsolutePath.EndsWith("/", StringComparison.OrdinalIgnoreCase)) {
          throw new ConfigurationErrorsException($"ApiRoot Uri must be absolute and end with a trailing '/'. '{apiRoot}' is invalid.");
        }
        return apiRoot;
      }
    });
    public Uri GitHubApiRoot => _gitHubApiRoot.Value;

    private Lazy<string> _gitHubClientId = new Lazy<string>(() => GetSetting("GitHubClientId"));
    public string GitHubClientId => _gitHubClientId.Value;

    private Lazy<string> _gitHubClientSecret = new Lazy<string>(() => GetSetting("GitHubClientSecret"));
    public string GitHubClientSecret => _gitHubClientSecret.Value;

    private Lazy<string> _gitHubLoggingStorage = new Lazy<string>(() => GetSetting("GitHubLoggingStorage"));
    public string GitHubLoggingStorage => _gitHubLoggingStorage.Value;

    private Lazy<string> _raygunApiKey = new Lazy<string>(() => GetSetting("RAYGUN_APIKEY"));
    public string RaygunApiKey => _raygunApiKey.Value;

    private Lazy<string> _mixpanelToken = new Lazy<string>(() => GetSetting("MixpanelToken"));
    public string MixpanelToken => _mixpanelToken.Value;

    private Lazy<string> _shipHubContext = new Lazy<string>(() => GetSetting("ShipHubContext", required: true));
    public string ShipHubContext => _shipHubContext.Value;

    private Lazy<string> _smtpPassword = new Lazy<string>(() => GetSetting("SmtpPassword", required: true));
    public string SmtpPassword => _smtpPassword.Value;

    private Lazy<string> _statHatKey = new Lazy<string>(() => GetSetting("StatHatKey"));
    public string StatHatKey => _statHatKey.Value;

    private Lazy<string> _statHatPrefix = new Lazy<string>(() => GetSetting("StatHatPrefix"));
    public string StatHatPrefix => _statHatPrefix.Value;

    private Lazy<bool> _useFiddler = new Lazy<bool>(() => {
#pragma warning disable IDE0018 // Inline variable declaration
      bool result;
#pragma warning restore IDE0018 // Inline variable declaration
      return bool.TryParse(GetSetting("UseFiddler"), out result) && result;
    });
    public bool UseFiddler => _useFiddler.Value;

    private Lazy<bool> _useSqlAzureExecutionStrategy = new Lazy<bool>(() => {
#pragma warning disable IDE0018 // Inline variable declaration
      bool result;
#pragma warning restore IDE0018 // Inline variable declaration
      return bool.TryParse(GetSetting("UseSqlAzureExecutionStrategy"), out result) && result;
    });
    public bool UseSqlAzureExecutionStrategy => _useSqlAzureExecutionStrategy.Value;

    private Lazy<string> _websiteHostName = new Lazy<string>(() => GetSetting("WebsiteHostName", required: true));
    public string WebsiteHostName => _websiteHostName.Value;

    private Lazy<string> _adminSecret = new Lazy<string>(() => GetSetting("AdminSecret", required: true));
    public string AdminSecret => _adminSecret.Value;

    private static string GetSetting(string key, bool required = false) {
      var value = CloudConfigurationManager.GetSetting(key);
      if (value == null && required) {
        throw new ConfigurationErrorsException($"'{key}' is required but not specified in configuration.");
      }
      return value;
    }

    private Lazy<string> _appleDeveloperMerchantIdDomainAssociation = new Lazy<string>(() => GetSetting("AppleDeveloperMerchantIdDomainAssociation"));
    public string AppleDeveloperMerchantIdDomainAssociation => _appleDeveloperMerchantIdDomainAssociation.Value;
  }
}
