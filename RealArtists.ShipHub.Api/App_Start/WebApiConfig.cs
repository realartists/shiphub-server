namespace RealArtists.ShipHub.Api {
  using System;
  using System.Net.Http.Formatting;
  using System.Web;
  using System.Web.Http;
  using System.Web.Http.ExceptionHandling;
  using Common;
  using Diagnostics;
  using Filters;
  using Mindscape.Raygun4Net.WebApi;

  public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
      config.Filters.Add(new DeaggregateExceptionFilterAttribute());
      config.Filters.Add(new ShipHubAuthenticationAttribute());
      config.Filters.Add(new AuthorizeAttribute());

      RaygunWebApiClient.Attach(config, GenerateRaygunClient);

      config.Formatters.Clear();
      config.Formatters.Add(new JsonMediaTypeFormatter());
      config.Formatters.JsonFormatter.SerializerSettings = JsonUtility.SaneDefaults;

      config.MapHttpAttributeRoutes();

      // Application Insights exception logging
      config.Services.Add(typeof(IExceptionLogger), new ApplicationInsightsExceptionLogger());
    }

    public static RaygunWebApiClient GenerateRaygunClient() {
      var client = new RaygunWebApiClient();
      client.AddWrapperExceptions(typeof(AggregateException));
      var user = HttpContext.Current?.User as ShipHubPrincipal;
      if (user != null) {
        client.User = $"{user.Login} ({user.UserId})";
      }
      return client;
    }
  }
}
