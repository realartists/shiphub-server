namespace RealArtists.ShipHub.Api.Middleware.ShipHubAuthentication {
  using System.Text.Encodings.Web;
  using Microsoft.AspNetCore.Authentication;
  using Microsoft.AspNetCore.Http;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  public class ShipHubAuthenticationMiddleware : AuthenticationMiddleware<ShipHubAuthenticationOptions> {
    public ShipHubAuthenticationMiddleware(
      RequestDelegate next,
      IOptions<ShipHubAuthenticationOptions> options,
      ILoggerFactory loggerFactory,
      UrlEncoder urlEncoder)
      : base(next, options, loggerFactory, urlEncoder) {
    }

    protected override AuthenticationHandler<ShipHubAuthenticationOptions> CreateHandler() {
      return new ShipHubAuthenticationHandler();
    }
  }
}
