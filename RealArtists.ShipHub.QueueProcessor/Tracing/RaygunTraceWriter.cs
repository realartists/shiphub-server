namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Collections;
  using System.Diagnostics;
  using Common;
  using Microsoft.Azure.WebJobs.Host;
  using Mindscape.Raygun4Net;

  public class RaygunTraceWriter : TraceWriter {
    private RaygunClient _raygunClient;

    public RaygunTraceWriter(string apiKey) : base(TraceLevel.Error) {
      _raygunClient = new RaygunClient(apiKey);
      _raygunClient.AddWrapperExceptions(typeof(AggregateException));
    }

    public override void Trace(TraceEvent traceEvent) {
      if (traceEvent.Exception != null) {
        var exception = traceEvent.Exception.Simplify();
        _raygunClient.SendInBackground(exception, null, traceEvent.Properties as IDictionary);
      }
    }
  }
}
