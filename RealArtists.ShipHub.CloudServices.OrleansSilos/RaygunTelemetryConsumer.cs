namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Common;
  using Mindscape.Raygun4Net;
  using Orleans.Runtime;

  public class RaygunTelemetryConsumer : IExceptionTelemetryConsumer {
    private RaygunClient _raygunClient;

    public RaygunTelemetryConsumer(string apiKey)
      : this(new RaygunClient(apiKey)) {
    }

    public RaygunTelemetryConsumer(RaygunClient client) {
      _raygunClient = client;
    }

    public void TrackException(Exception exception, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null) {
      var simplified = exception.Simplify();

      var props = properties as IDictionary;
      if (props == null && properties != null && properties.Any()) {
        props = properties.ToDictionary(x => x.Key, x => x.Value);
      }

      _raygunClient.SendInBackground(simplified, null, props);
    }

    public void Close() {
    }

    public void Flush() {
    }
  }
}
