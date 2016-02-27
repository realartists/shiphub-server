namespace RealArtists.ShipHub.Api.Middleware.ShipHubAuthentication {
  using System;
  using Microsoft.AspNetCore.Builder;
  using Microsoft.Extensions.Options;

  /// <summary>
  /// Extension methods to add ShipHub authentication capabilities to an HTTP application pipeline.
  /// </summary>
  public static class ShipHubAppBuilderExtensions {
    /// <summary>
    /// Adds the <see cref="ShipHubAuthenticationMiddleware"/> middleware to the specified <see cref="IApplicationBuilder"/>, which enables cookie authentication capabilities.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
    /// <returns>A reference to this instance after the operation has completed.</returns>
    public static IApplicationBuilder UseShipHubAuthentication(this IApplicationBuilder app) {
      if (app == null) {
        throw new ArgumentNullException(nameof(app));
      }

      return app.UseMiddleware<ShipHubAuthenticationMiddleware>();
    }

    /// <summary>
    /// Adds the <see cref="CookieAuthenticationMiddleware"/> middleware to the specified <see cref="IApplicationBuilder"/>, which enables cookie authentication capabilities.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
    /// <param name="options">A <see cref="CookieAuthenticationOptions"/> that specifies options for the middleware.</param>
    /// <returns>A reference to this instance after the operation has completed.</returns>
    public static IApplicationBuilder UseShipHubAuthentication(this IApplicationBuilder app, ShipHubAuthenticationOptions options) {
      if (app == null) {
        throw new ArgumentNullException(nameof(app));
      }
      if (options == null) {
        throw new ArgumentNullException(nameof(options));
      }

      return app.UseMiddleware<ShipHubAuthenticationMiddleware>(Options.Create(options));
    }
  }
}
