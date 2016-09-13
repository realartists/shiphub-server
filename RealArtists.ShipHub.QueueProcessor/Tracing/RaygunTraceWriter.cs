namespace RealArtists.ShipHub.QueueProcessor.Tracing {
  using System.Collections;
  using System.Diagnostics;
  using Common;
  using Microsoft.Azure.WebJobs.Host;
  using Mindscape.Raygun4Net;

  public class RaygunTraceWriter : TraceWriter {
    private RaygunClient _raygunClient;

    public RaygunTraceWriter(RaygunClient client) : base(TraceLevel.Error) {
      _raygunClient = client;
    }

    public override void Trace(TraceEvent traceEvent) {
      if (traceEvent.Exception != null) {
        var exception = traceEvent.Exception.Simplify();
        if (exception.InnerException.GetType() != typeof(TraceBypassException)) {
          _raygunClient.SendInBackground(exception, null, traceEvent.Properties as IDictionary);
        }
      }
    }
  }
}
