namespace RealArtists.ShipHub.QueueProcessor {
  using System.Diagnostics;
  using System.Linq;
  using Common;
  using Microsoft.ApplicationInsights;
  using Microsoft.Azure.WebJobs.Host;

  public class ApplicationInsightsTraceWriter : TraceWriter {
    private TelemetryClient _client = new TelemetryClient();

    public ApplicationInsightsTraceWriter() : base(TraceLevel.Error) {
    }

    public override void Trace(TraceEvent traceEvent) {
      if (traceEvent.Exception != null) {
        var exception = traceEvent.Exception.Simplify();
        var properties = traceEvent.Properties?.ToDictionary(x => x.Key, x => x.Value?.ToString());
        _client.TrackException(exception, properties);
      }
    }
  }
}
