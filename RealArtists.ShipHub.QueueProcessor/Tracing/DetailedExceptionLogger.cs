namespace RealArtists.ShipHub.QueueProcessor.Tracing {
  using System;
  using System.Collections.Generic;
  using Common;
  using Microsoft.ApplicationInsights;
  using Mindscape.Raygun4Net;
  using Mindscape.Raygun4Net.Messages;

  public interface IDetailedExceptionLogger {
    void Log(Guid functionInstanceId, long? forUserId, object message, Exception exception);
  }

  public class DetailedExceptionLogger : IDetailedExceptionLogger {
    private TelemetryClient _aiClient;
    private RaygunClient _raygunClient;

    /// <summary>
    /// Default instance does nothing.
    /// </summary>
    public DetailedExceptionLogger() : this(null, null) { }

    public DetailedExceptionLogger(TelemetryClient telemetryClient, RaygunClient raygunClient) {
      _aiClient = telemetryClient;
      _raygunClient = raygunClient;
    }

    public void Log(Guid functionInstanceId, long? forUserId, object message, Exception exception) {
      // This should be fast and queue if needed.
      var ex = exception.Simplify();

      var props = new Dictionary<string, string>() {
        { "functionInstanceId", functionInstanceId.ToString() },
        { "forUserId", forUserId?.ToString() },
        { "timestamp", DateTime.UtcNow.ToString("o") },
        { "message", message.SerializeObject() },
      };

      _raygunClient?.SendInBackground(ex, null, props, new RaygunIdentifierMessage(forUserId?.ToString()));
      _aiClient?.TrackException(ex, props);
    }
  }
}
