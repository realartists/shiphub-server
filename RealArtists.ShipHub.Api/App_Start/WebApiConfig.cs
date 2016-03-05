namespace RealArtists.ShipHub.Api {
  using System.Web.Http;

  public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
      config.MapHttpAttributeRoutes();
    }
  }
}
