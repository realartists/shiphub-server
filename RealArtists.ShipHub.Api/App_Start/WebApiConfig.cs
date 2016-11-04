namespace RealArtists.ShipHub.Api {
  using System;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Web;
  using System.Web.Http;
  using System.Web.Http.ExceptionHandling;
  using Common;
  using Controllers;
  using Diagnostics;
  using Filters;
  using Microsoft.Azure;
  using Mindscape.Raygun4Net.WebApi;

  public static class WebApiConfig {
    private static readonly string RaygunApiKey = CloudConfigurationManager.GetSetting("RAYGUN_APIKEY");

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

    public static RaygunWebApiClient GenerateRaygunClient(HttpRequestMessage requestMessage) {
      RaygunWebApiClient client = null;
      if (RaygunApiKey.IsNullOrWhiteSpace()) {
        // Use default.
        client = new RaygunWebApiClient();
      } else {
        client = new RaygunWebApiClient(RaygunApiKey);
      }

      client.AddWrapperExceptions(typeof(AggregateException));

      if (requestMessage.GetActionDescriptor()?.ControllerDescriptor?.ControllerType == typeof(GitHubWebhookController)) {
        // Webhook calls from GitHub don't really have a user, but we'd still like for Raygun
        // to show us how many unique orgs + repos are affected by a given error.  We can abuse
        // the User concept to get this.
        client.User = requestMessage.RequestUri.PathAndQuery.Replace('/', '_');
      } else {
        var user = HttpContext.Current?.User as ShipHubPrincipal;
        if (user != null) {
          client.User = user.DebugIdentifier;
        }
      }

      return client;
    }
  }
}
