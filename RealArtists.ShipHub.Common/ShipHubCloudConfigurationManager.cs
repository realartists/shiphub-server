namespace RealArtists.ShipHub.Common {
  using System.Configuration;
  using Microsoft.Azure;

  public static class ShipHubCloudConfigurationManager {
    public static string GetSetting(string key) {
      string value = CloudConfigurationManager.GetSetting(key);
      if (value == null) {
        throw new ConfigurationErrorsException($"'{key}' not specified in configuration.");
      }
      return value;
    }
  }
}
