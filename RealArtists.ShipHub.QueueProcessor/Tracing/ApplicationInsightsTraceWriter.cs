namespace RealArtists.ShipHub.QueueProcessor.Tracing {
  using System.Diagnostics;
  using System.Linq;
  using Common;
  using Microsoft.ApplicationInsights;
  using Microsoft.Azure.WebJobs.Host;

  public class ApplicationInsightsTraceWriter : TraceWriter {
    private TelemetryClient _client;

    public ApplicationInsightsTraceWriter(TelemetryClient client) : base(TraceLevel.Error) {
      _client = client;
    }

    public override void Trace(TraceEvent traceEvent) {
      if (traceEvent.Exception != null) {
        var exception = traceEvent.Exception.Simplify();
        if (exception.InnerException.GetType() != typeof(TraceBypassException)) {
          var properties = traceEvent.Properties?.ToDictionary(x => x.Key, x => x.Value?.ToString());
          _client.TrackException(exception, properties);
        }
      }
    }
  }
}
