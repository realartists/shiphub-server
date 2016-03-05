namespace RealArtists.ShipHub.Api {
  using System;
  using System.Net.Http.Formatting;
  using System.Web.Http;
  using Mindscape.Raygun4Net.WebApi;
  using Utilities;

  public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
      //config.Filters.Add(new DeaggregateExceptionFilterAttribute());
      //config.Filters.Add(new ShipAuthenticationAttribute());
      //config.Filters.Add(new AuthorizeAttribute());

      RaygunWebApiClient.Attach(config, GenerateRaygunClient);

      config.Formatters.Clear();
      config.Formatters.Add(new JsonMediaTypeFormatter());
      config.Formatters.JsonFormatter.SerializerSettings = JsonUtility.SaneDefaults;

      //config.MapHttpAttributeRoutes(new CustomDirectRouteProvider());
      config.MapHttpAttributeRoutes();

      // Application Insights exception logging
      //config.Services.Add(typeof(IExceptionLogger), new ApplicationInsightsExceptionLogger());
    }

    //public class CustomDirectRouteProvider : DefaultDirectRouteProvider {
    //  protected override IReadOnlyList<IDirectRouteFactory> GetActionRouteFactories(HttpActionDescriptor actionDescriptor) {
    //    if (typeof(ShipApiController).IsAssignableFrom(actionDescriptor.ControllerDescriptor.ControllerType)) {
    //      return actionDescriptor.GetCustomAttributes<IDirectRouteFactory>(inherit: true);
    //    }

    //    return actionDescriptor.GetCustomAttributes<IDirectRouteFactory>(inherit: false);
    //  }
    //}

    public static RaygunWebApiClient GenerateRaygunClient() {
      var client = new RaygunWebApiClient();
      client.AddWrapperExceptions(typeof(AggregateException));
      //var user = HttpContext.Current?.User as ShipPrincipal;
      //if (user != null) {
      //  client.User = $"{user.UserId}/{user.OrganizationId}";
      //}
      return client;
    }
  }
}

