namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Runtime.CompilerServices;
  using Microsoft.ApplicationInsights;
  using Microsoft.ApplicationInsights.Extensibility;
  using Mindscape.Raygun4Net;
  using Mindscape.Raygun4Net.Messages;

  public static class ExceptionUtilities {
    public static Exception Simplify(this Exception exception) {

      if (exception is AggregateException agg) {
        agg = agg.Flatten();

        if (agg.InnerExceptions.Count == 1) {
          return Simplify(agg.InnerExceptions.Single());
        }
      }

      return exception;
    }

    private static Lazy<RaygunClient> _raygunClient = new Lazy<RaygunClient>(() => {
      if (!ShipHubCloudConfiguration.Instance.RaygunApiKey.IsNullOrWhiteSpace()) {
        return new RaygunClient(ShipHubCloudConfiguration.Instance.RaygunApiKey);
      } else {
        return null;
      }
    });

    private static Lazy<TelemetryClient> _aiClient = new Lazy<TelemetryClient>(() => {
      if (!ShipHubCloudConfiguration.Instance.ApplicationInsightsKey.IsNullOrWhiteSpace()) {
        TelemetryConfiguration.Active.InstrumentationKey = ShipHubCloudConfiguration.Instance.ApplicationInsightsKey;
        return new TelemetryClient();
      } else {
        return null;
      }
    });

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public static void Report(this Exception exception, string message = null, string userInfo = null, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      try {
        var ex = exception.Simplify();

        {
          var m = message;
          if (m == null && !userInfo.IsNullOrWhiteSpace()) {
            m = $"userId: ${userInfo}";
          }
          Log.Exception(ex, message);
        }

        var props = new Dictionary<string, string>() {
          { "user", userInfo },
          { "timestamp", DateTime.UtcNow.ToString("o") },
          { "message", message },
          { "memberName", memberName },
          { "sourceFilePath", filePath },
          { "sourceLineNumber", lineNumber.ToString() },
        };

        _raygunClient.Value?.SendInBackground(ex, null, props, new RaygunIdentifierMessage(userInfo));
        _aiClient.Value?.TrackException(ex, props);
      } catch { /*nah*/ }
    }
  }
}
