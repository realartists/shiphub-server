namespace RealArtists.ShipHub.Api.Diagnostics {
  using System.Collections.Generic;
  using System.Web.Http.ExceptionHandling;
  using Filters;
  using Microsoft.ApplicationInsights;

  public class ApplicationInsightsExceptionLogger : ExceptionLogger {
    private TelemetryClient _client = new TelemetryClient();

    public override void Log(ExceptionLoggerContext context) {
      IDictionary<string, string> properties = null;

      if (context.RequestContext.Principal is ShipHubPrincipal user) {
        properties = new Dictionary<string, string> {
          { "Login", user.Login },
          { "UserId", user.UserId.ToString() },
          { "DebugIdentifier", user.DebugIdentifier },
        };
      }

      _client.TrackException(context.Exception, properties);

      base.Log(context);
    }
  }
}
