namespace RealArtists.ShipHub.Api {
  using Common;
  using Microsoft.ApplicationInsights.Extensibility;

  public static class ApplicationInsightsConfig {
    public static void Register(string instrumentationKey) {
      if (!instrumentationKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = instrumentationKey;
      }
    }
  }
}
