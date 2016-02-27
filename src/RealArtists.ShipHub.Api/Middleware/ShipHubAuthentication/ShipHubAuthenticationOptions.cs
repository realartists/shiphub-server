namespace RealArtists.ShipHub.Api.Middleware.ShipHubAuthentication {
  using Microsoft.AspNetCore.Builder;
  using Microsoft.Extensions.Options;

  /// <summary>
  /// Contains the options used by the ShipHubAuthenticationMiddleware
  /// </summary>
  public class ShipHubAuthenticationOptions : AuthenticationOptions, IOptions<ShipHubAuthenticationOptions> {
    /// <summary>
    /// Create an instance of the options initialized with the default values
    /// </summary>
    public ShipHubAuthenticationOptions() {
      AuthenticationScheme = ShipHubAuthenticationDefaults.AuthenticationScheme;
      AutomaticAuthenticate = true;
      AutomaticChallenge = false;
    }

    public ShipHubAuthenticationOptions Value { get { return this; } }
  }
}
