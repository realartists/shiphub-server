namespace RealArtists.ShipHub.Api {
  using System.Web;
  using System.Web.Http;
  using Common;
  using Microsoft.Azure;

  public class WebApiApplication : HttpApplication {
    protected void Application_Start() {
      var shipHubConfig = new ShipHubCloudConfiguration();
      ApplicationInsightsConfig.Register(shipHubConfig.ApplicationInsightsKey);
      GlobalConfiguration.Configure((config) => WebApiConfig.Register(config, shipHubConfig.RaygunApiKey));
      GlobalConfiguration.Configure(SimpleInjectorConfig.Register);

      var chargeBeeHostAndApiKey = shipHubConfig.ChargeBeeHostAndKey;
      if (!chargeBeeHostAndApiKey.IsNullOrWhiteSpace()) {
        var parts = chargeBeeHostAndApiKey.Split(':');
        ChargeBee.Api.ApiConfig.Configure(parts[0], parts[1]);
      }
    }
  }
}
