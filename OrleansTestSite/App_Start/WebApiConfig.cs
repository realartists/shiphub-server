using System.Net.Http.Formatting;
using System.Web.Http;

namespace OrleansTestSite {
  public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
      config.Formatters.Clear();
      config.Formatters.Add(new JsonMediaTypeFormatter());

      // Web API routes
      config.MapHttpAttributeRoutes();
    }
  }
}
