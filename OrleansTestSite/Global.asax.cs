using System.Web.Http;

namespace OrleansTestSite {
  public class WebApiApplication : System.Web.HttpApplication {
    protected void Application_Start() {
      GlobalConfiguration.Configure(WebApiConfig.Register);
    }
  }
}
