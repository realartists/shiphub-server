namespace RealArtists.ShipHub.Api {
  using System.Web;
  using System.Web.Http;

  public class WebApiApplication : HttpApplication {
    protected void Application_Start() {
      ApplicationInsightsConfig.Register();
      GlobalConfiguration.Configure(WebApiConfig.Register);
    }
  }
}
