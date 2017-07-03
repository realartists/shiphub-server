namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using Common;
  using Orleans.Runtime;

  // Current Orleans telemetry interfaces:
  // ITraceTelemetryConsumer, IEventTelemetryConsumer, IExceptionTelemetryConsumer, IDependencyTelemetryConsumer, IMetricTelemetryConsumer, IRequestTelemetryConsumer

  public class StatHatTelemetryConsumer : IExceptionTelemetryConsumer, IMetricTelemetryConsumer {
    private static Lazy<string> _hostName = new Lazy<string>(() => {
      return Dns.GetHostName();
    });

    public string HostName => _hostName.Value;

    public void TrackException(Exception exception, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null) {
      var type = exception.GetType();
      ReportCount("Exception");
      ReportCount($"Exception.{type.Name}");
    }

    public void TrackMetric(string name, double value, IDictionary<string, string> properties = null) {
      ReportValue(name, value);
    }

    public void TrackMetric(string name, TimeSpan value, IDictionary<string, string> properties = null) {
      ReportValue(name, value.TotalMilliseconds);
    }

    private string StatName(string name) {
      return $"SiloTelemetry.{HostName}.{name}";
    }

    private void ReportCount(string name, long count = 1, DateTimeOffset? timestamp = null) {
      StatHat.Count(StatName(name), count, timestamp ?? DateTimeOffset.UtcNow);
    }

    private void ReportValue(string name, double value, DateTimeOffset? timestamp = null) {
      StatHat.Value(StatName(name), value, timestamp ?? DateTimeOffset.UtcNow);
    }

    public void DecrementMetric(string name) { /* NOP */ }

    public void DecrementMetric(string name, double value) { /* NOP */ }

    public void IncrementMetric(string name) { /* NOP */ }

    public void IncrementMetric(string name, double value) { /* NOP */ }

    public void Flush() { /* NOP */ }

    public void Close() { /* NOP */ }
  }
}
