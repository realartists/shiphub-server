namespace RealArtists.ShipHub.Api {
  using System.Web;
  using System.Web.Http;
  using Common;

  public class WebApiApplication : HttpApplication {
    protected void Application_Start() {
      Log.Trace();

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointDefaultConnectionLimit();

      var shipHubConfig = new ShipHubCloudConfiguration();
      ApplicationInsightsConfig.Register(shipHubConfig.ApplicationInsightsKey);
      GlobalConfiguration.Configure((config) => WebApiConfig.Register(config, shipHubConfig.RaygunApiKey));
      SimpleInjectorConfig.Register(shipHubConfig);
    }
  }
}
