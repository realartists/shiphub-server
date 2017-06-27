namespace RealArtists.ShipHub.Api {
  using System;
  using System.IO;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Threading.Tasks;
  using System.Web;
  using System.Web.Http;
  using System.Web.Http.ExceptionHandling;
  using Common;
  using Controllers;
  using Diagnostics;
  using Filters;
  using Mindscape.Raygun4Net.WebApi;

  public static class WebApiConfig {
    public static void Register(HttpConfiguration config, string raygunApiKey) {
      config.Filters.Add(new DeaggregateExceptionFilterAttribute());
      config.Filters.Add(new ShipHubAuthenticationAttribute());
      config.Filters.Add(new AuthorizeAttribute());
      config.Filters.Add(new CommonLogActionFilterAttribute());

      RaygunWebApiClient.Attach(config, RaygunClientFactory(raygunApiKey));

      config.Formatters.Clear();
      config.Formatters.Add(new ChunkedJsonMediaTypeFormatter() { SerializerSettings = JsonUtility.JsonSerializerSettings });

      config.MapHttpAttributeRoutes();

      // Application Insights exception logging
      config.Services.Add(typeof(IExceptionLogger), new ApplicationInsightsExceptionLogger());
      // Common logging for exceptions
      config.Services.Add(typeof(IExceptionLogger), new CommonLogExceptionLogger());
    }

    public static Func<HttpRequestMessage, RaygunWebApiClient> RaygunClientFactory(string raygunApiKey) {
      return (HttpRequestMessage requestMessage) => {
        RaygunWebApiClient client = null;
        if (raygunApiKey.IsNullOrWhiteSpace()) {
          // Use default.
          client = new RaygunWebApiClient();
        } else {
          client = new RaygunWebApiClient(raygunApiKey);
        }

        client.AddWrapperExceptions(typeof(AggregateException));

        if (requestMessage.GetActionDescriptor()?.ControllerDescriptor?.ControllerType == typeof(GitHubWebhookController)) {
          // Webhook calls from GitHub don't really have a user, but we'd still like for Raygun
          // to show us how many unique orgs + repos are affected by a given error.  We can abuse
          // the User concept to get this.
          client.User = requestMessage.RequestUri.PathAndQuery.Replace('/', '_');
        } else {
          if (HttpContext.Current?.User is ShipHubPrincipal user) {
            client.User = user.DebugIdentifier;
          }
        }

        return client;
      };
    }
  }

  public class ChunkedJsonMediaTypeFormatter : JsonMediaTypeFormatter {
    public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger) {
      // Work around WebApi bug that can't handle Chunked Transfer Encoding.
      // http://stackoverflow.com/questions/26111850/asp-net-web-api-the-framework-is-not-converting-json-to-object-when-using-chun
      // https://gist.github.com/cobysy/578302d0f4f5b895f459
      // https://github.com/ASP-NET-MVC/aspnetwebstack/blob/master/src/System.Net.Http.Formatting/Formatting/BaseJsonMediaTypeFormatter.cs#L206
      if (content?.Headers?.ContentType != null && content?.Headers?.ContentLength == 0) {
        content.Headers.ContentLength = null;
      }
      return base.ReadFromStreamAsync(type, readStream, content, formatterLogger);
    }
  }
}
