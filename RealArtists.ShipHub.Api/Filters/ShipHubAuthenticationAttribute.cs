namespace RealArtists.ShipHub.Api.Filters {
  using System;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Security.Claims;
  using System.Security.Principal;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Filters;
  using Common.DataModel;

  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
  public sealed class ShipHubAuthenticationAttribute : FilterAttribute, IAuthenticationFilter {
    public const string AuthenticationScheme = "token";

    public async Task AuthenticateAsync(HttpAuthenticationContext context, CancellationToken cancellationToken) {
      var token = ExtractToken(context.Request);

      if (token != null) {
        var principal = await ValidateToken(token);

        if (principal != null) {
          context.Principal = principal;
        }
      }
    }

    public Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken) {
      context.Result = new ShipHubChallengeWrapperResult(AuthenticationScheme, context.Result);
      return Task.CompletedTask;
    }

    public static async Task<ShipHubPrincipal> ValidateToken(string token) {
      using (var s = new ShipHubContext()) {
        var user = await s.Users
          .SingleOrDefaultAsync(x => x.Token == token)
          .ConfigureAwait(false);

        if (user == null) {
          return null;
        }

        return new ShipHubPrincipal(user.Id, user.Login, user.Token);
      }
    }

    public static string ExtractToken(HttpRequestMessage request) {
      // Prefer Authorization header, fall back to Cookie, then QueryString.
      // Normal Header
      var authorization = request.Headers.Authorization;
      if (authorization?.Scheme == AuthenticationScheme) {
        return authorization.Parameter;
      }

      return null;
    }
  }

  public static class ShipHubClaimTypes {
    public const string UserId = "ShipHub-UserId";
    public const string Login = "ShipHub-Login";
    public const string Token = "ShipHub-Token";
  }

  public static class ShipHubUserExtensions {
    public static ShipHubPrincipal AsShip(this IPrincipal user) {
      return user as ShipHubPrincipal;
    }
  }

  public class ShipHubIdentity : ClaimsIdentity {
    public ShipHubIdentity(long userId, string login, string token)
       : base("ShipHub") {
      AddClaims(new Claim[] {
         // Begin required for CSRF tokens
        new Claim(ClaimTypes.NameIdentifier, $"com.realartists.shipHub/User/{userId}"),
        new Claim("http://schemas.microsoft.com/accesscontrolservice/2010/07/claims/identityprovider", "com.realartists.ship/ShipHubIdentity"),
         // End required for CSRF tokens
        new Claim(ClaimTypes.Name, login),
        new Claim(ShipHubClaimTypes.UserId, userId.ToString()),
        new Claim(ShipHubClaimTypes.Login, login),
        new Claim(ShipHubClaimTypes.Token, token),
      });
    }
  }

  public class ShipHubPrincipal : ClaimsPrincipal {
    public ShipHubPrincipal(long userId, string login, string token)
      : base(new ShipHubIdentity(userId, login, token)) {
      UserId = userId;
      Login = login;
      Token = token;
    }

    public long UserId { get; private set; }
    public string Login { get; private set; }
    public string Token { get; private set; }
  }

  public class ShipHubChallengeWrapperResult : IHttpActionResult {
    private AuthenticationHeaderValue _challenge;
    private IHttpActionResult _innerResult;

    public ShipHubChallengeWrapperResult(string scheme, IHttpActionResult innerResult) {
      _challenge = new AuthenticationHeaderValue(scheme);
      _innerResult = innerResult;
    }

    public async Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken) {
      var innerResponse = await _innerResult.ExecuteAsync(cancellationToken);
      var authHeaders = innerResponse.Headers.WwwAuthenticate;

      if (innerResponse.StatusCode == HttpStatusCode.Unauthorized) {
        if (!authHeaders.Any(h => h.Scheme == _challenge.Scheme)) {
          authHeaders.Add(_challenge);
        }
      }

      return innerResponse;
    }
  }
}
