namespace RealArtists.ShipHub.Api {
  using System.Web;
  using System.Web.Http;
  using Common;
  using Microsoft.Azure;

  public class WebApiApplication : HttpApplication {
    protected void Application_Start() {
      ApplicationInsightsConfig.Register();
      GlobalConfiguration.Configure(WebApiConfig.Register);
      GlobalConfiguration.Configure(SimpleInjectorConfig.Register);

      var chargeBeeHostAndApiKey = CloudConfigurationManager.GetSetting("ChargeBeeHostAndKey");
      if (!chargeBeeHostAndApiKey.IsNullOrWhiteSpace()) {
        var parts = chargeBeeHostAndApiKey.Split(':');
        ChargeBee.Api.ApiConfig.Configure(parts[0], parts[1]);
      }
    }
  }
}
