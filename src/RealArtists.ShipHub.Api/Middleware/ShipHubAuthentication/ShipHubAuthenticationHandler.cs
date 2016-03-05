namespace RealArtists.ShipHub.Api.Middleware.ShipHubAuthentication {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using System.Security.Claims;
  using Microsoft.AspNetCore.Builder;
  using Microsoft.AspNetCore.Http;
  using Microsoft.AspNetCore.Http.Authentication;
  using Microsoft.AspNetCore.Http.Features;
  using Microsoft.AspNetCore.Http.Features.Authentication;
  using Microsoft.Extensions.Primitives;
  using Microsoft.Net.Http.Headers;
  using Microsoft.AspNetCore.Authentication;

  public class ShipHubAuthenticationHandler : AuthenticationHandler<ShipHubAuthenticationOptions> {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

      return AuthenticateResult.Skip();
    }

    // TODO: Should I handle SignIn and SignOut here?
    // Doesn't seem that clean, actually.
  }
}
