namespace RealArtists.ShipHub.Api {
  using Common;
  using Microsoft.ApplicationInsights.Extensibility;
  using Microsoft.Azure;

  public static class ApplicationInsightsConfig {
    public const string ApplicationInsightsKey = "APPINSIGHTS_INSTRUMENTATIONKEY";
    public static void Register() {
      var instrumentationKey = CloudConfigurationManager.GetSetting(ApplicationInsightsKey);
      if (!instrumentationKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = instrumentationKey;
      }
    }
  }
}
