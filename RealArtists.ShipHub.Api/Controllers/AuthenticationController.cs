namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using ActorInterfaces.GitHub;
  using Actors.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using d = Common.DataModel;
  using g = Common.GitHub.Models;

  public class LoginRequest {
    public string AccessToken { get; set; }
    public string ApplicationId { get; set; }
    public string ClientName { get; set; }

    [JsonProperty("repoPrefs")]
    public SyncSettings SyncSettings { get; set; }
  }

  public class CreatedAccessToken {
    public string AccessToken { get; set; }
    public string Scope { get; set; }
    public string TokenType { get; set; }
  }

  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubApiController, IGitHubClient {
    private static readonly ImmutableHashSet<string> PrivateScopes = ImmutableHashSet.Create(
      "admin:org_hook",
      "admin:repo_hook",
      "read:org",
      "repo", // grants access to notifications
      "user:email");

    private static readonly ImmutableHashSet<string> PublicScopes = ImmutableHashSet.Create(
      "admin:org_hook",
      "admin:repo_hook",
      "notifications",
      "public_repo",
      "read:org",
      "user:email");

    private static readonly IGitHubHandler _handlerPipeline = new GitHubHandler();

    private IShipHubConfiguration _config;
    private IAsyncGrainFactory _grainFactory;
    private IMapper _mapper;

    private static readonly IReadOnlyList<ImmutableHashSet<string>> _validScopesCollection = new List<ImmutableHashSet<string>>() {
      PrivateScopes,
      PublicScopes,
    }.AsReadOnly();

    public AuthenticationController(IShipHubConfiguration config, IAsyncGrainFactory grainFactory, IMapper mapper) {
      _config = config;
      _grainFactory = grainFactory;
      _mapper = mapper;
    }

    // ///////////////////////////////////////////////////
    // Gross hack until I can fix UserActor to use tokens
    // ///////////////////////////////////////////////////

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
    public Uri ApiRoot { get; } = ShipHubCloudConfiguration.Instance.GitHubApiRoot;
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public long UserId { get; } = -1;
    public string UserInfo { get; } = "ShipHub Authentication Controller (-1)";

    public string AccessToken { get; private set; }

    public int NextRequestId() {
      return 1;
    }

    private Task<GitHubResponse<g.Account>> GitHubUser(string accessToken, CancellationToken cancellationToken) {
      AccessToken = accessToken;
      var request = new GitHubRequest("user");
      return _handlerPipeline.Fetch<g.Account>(this, request, cancellationToken);
    }

    // ///////////////////////////////////////////////////
    // For grant revocation
    // ///////////////////////////////////////////////////

    private static HttpClient _AppClient = CreateGitHubAppHttpClient();

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static HttpClient CreateGitHubAppHttpClient() {
      var config = ShipHubCloudConfiguration.Instance;

#if DEBUG
      var handler = HttpUtilities.CreateDefaultHandler(config.UseFiddler);
#else
      var handler = HttpUtilities.CreateDefaultHandler();
#endif

      var httpClient = new HttpClient(handler, true) {
        Timeout = TimeSpan.FromSeconds(50),
      };

      var headers = httpClient.DefaultRequestHeaders;
      headers.AcceptEncoding.Clear();
      headers.AcceptEncoding.ParseAdd("gzip");
      headers.AcceptEncoding.ParseAdd("deflate");

      headers.Accept.Clear();
      headers.Accept.ParseAdd("application/vnd.github.v3+json");

      headers.AcceptCharset.Clear();
      headers.AcceptCharset.ParseAdd("utf-8");

      headers.Add("Time-Zone", "Etc/UTC");

      headers.UserAgent.Clear();
      headers.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));

      var basicAuth = $"{config.GitHubClientId}:{config.GitHubClientSecret}";
      var basicBytes = Encoding.ASCII.GetBytes(basicAuth);
      var basic64 = Convert.ToBase64String(basicBytes);
      headers.Authorization = new AuthenticationHeaderValue("basic", basic64);

      return httpClient;
    }

    private async Task<bool> RevokeGrant(string accessToken) {
      var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(ApiRoot, $"applications/{_config.GitHubClientId}/grants/{accessToken}"));
      var response = await _AppClient.SendAsync(request);
      return response.StatusCode == HttpStatusCode.NoContent;
    }

    private static readonly Uri _OauthTokenRedemption = new Uri("https://github.com/login/oauth/access_token");
    private async Task<CreatedAccessToken> CreateAccessToken(string code, string state) {
      var body = new {
        ClientId = _config.GitHubClientId,
        ClientSecret = _config.GitHubClientSecret,
        Code = code,
        State = state,
      };

      var httpRequest = new HttpRequestMessage(HttpMethod.Post, _OauthTokenRedemption) {
        Content = new ObjectContent<object>(body, GitHubSerialization.JsonMediaTypeFormatter, GitHubSerialization.JsonMediaType)
      };
      httpRequest.Headers.Accept.Clear();
      httpRequest.Headers.Accept.ParseAdd("application/json");

      var response = await _AppClient.SendAsync(httpRequest);

      if (response.IsSuccessStatusCode) {
        var temp = await response.Content.ReadAsAsync<JToken>(GitHubSerialization.MediaTypeFormatters);
        if (temp["error"] != null) {
          throw new Exception(temp.ToString());
        } else {
          return temp.ToObject<CreatedAccessToken>(GitHubSerialization.JsonSerializer);
        }
      } else {
        throw new HttpResponseException(response.StatusCode);
      }
    }

    // ///////////////////////////////////////////////////
    // Web calls
    // ///////////////////////////////////////////////////

    [HttpPost]
    [AllowAnonymous]
    [Route("lambda_legacy")]
    public async Task<IHttpActionResult> LambdaLegacy([FromBody] JToken body, CancellationToken cancellationToken) {
      var code = body?["code"]?.Value<string>();
      if (code.IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(code)} is required.");
      }

      var token = await CreateAccessToken(code, null);

      /* Some exposition:
       * 
       * I go ahead and create the account here even though the client is going to call
       * /login in just a bit. Before it does, it will request the user's sync settings.
       * We need to ensure that authorization succeeds *and* the correct settings are
       * returned, even if this is a new token for an existing user.
       * 
       * It's gross, but this will work and retain both backward and forward compatibility.
       * 
       * NOTE WELL: This logic CANNOT replace the /login logic, since older clients still
       * use the Amazon Lambda endpoint and don't call this endpoint before logging in.
       */

      var login = await LoginCommon(token.AccessToken, cancellationToken);

      // Notably (and intentionally) does not start a sync yet.
      // Clients using this flow should check for and set SyncSettings before they
      // open the web socket.

      if (login.ErrorResult != null) {
        return login.ErrorResult;
      } else {
        // This uses the GitHub serialization style because the old lambda code
        // just relayed GitHub's response.
        return Json(new {
          Token = token.AccessToken,
          User = login.UserInfo,
        }, GitHubSerialization.JsonSerializerSettings);
      }
    }

    [HttpDelete]
    [Route("login")]
    public async Task<IHttpActionResult> Logout() {
      // User wants to log out.
      using (var context = new d.ShipHubContext()) {
        var hookDetails = await context.GetLogoutWebhooks(ShipHubUser.UserId);
        var github = await _grainFactory.GetGrain<IGitHubActor>(ShipHubUser.UserId);
        var tasks = new List<Task>();

        // Delete all repo hooks where they're the only user
        tasks.AddRange(hookDetails.RepositoryHooks.Select(x => github.DeleteRepositoryWebhook(x.Name, x.HookId, RequestPriority.Interactive)));
        // Delete all org hooks where they're the only user
        tasks.AddRange(hookDetails.OrganizationHooks.Select(x => github.DeleteOrganizationWebhook(x.Name, x.HookId, RequestPriority.Interactive)));

        // Wait and log errors.
        var userInfo = $"{ShipHubUser.Login} ({ShipHubUser.UserId})";
        try {
          // Ensure requests complete before we revoke our access below
          await Task.WhenAll(tasks);
          foreach (var task in tasks) {
            task.LogFailure(userInfo);
          }
        } catch {
          // They're logging out. We had our chance.
        }

        var tokens = await context.Tokens
          .Where(x => x.UserId == ShipHubUser.UserId)
          .Select(x => x.Token)
          .ToArrayAsync();
        // Try all the tokens.
        foreach (var token in tokens) {
          RevokeGrant(token).LogFailure(userInfo);
        }

        // Invalidate their token with ShipHub
        await context.RevokeAccessTokens(ShipHubUser.UserId);
      }

      return Ok();
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("login")]
    public async Task<IHttpActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken) {
      if ((request?.AccessToken).IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.AccessToken)} is required.");
      }
      if (request.ClientName.IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      d.User user = null;
      g.Account userInfo;
      IHttpActionResult errorResult = null;

      // Newer clients will have already authenticated via lambda_legacy
      // They don't send the authorization header though, so let's check
      // if we recognize this token.
      using (var context = new d.ShipHubContext()) {
        user = await context.Tokens
          .AsNoTracking()
          .Where(x => x.Token == request.AccessToken)
          .Select(x => x.User)
          .SingleOrDefaultAsync();
      }

      if (user != null) {
        // We know this user already.
        userInfo = new g.Account() {
          Id = user.Id,
          Login = user.Login,
          Name = user.Name,
          Type = g.GitHubAccountType.User,
        };
      } else { // End goal is to make everything below obsolete.
        var login = await LoginCommon(request.AccessToken, cancellationToken);
        userInfo = login.UserInfo;
        errorResult = login.ErrorResult;
      }

      if (errorResult != null) {
        // Abort early on error
        return errorResult;
      }

      // Save settings if sent
      if (request.SyncSettings != null) {
        using (var context = new d.ShipHubContext()) {
          await context.SetAccountSettings(userInfo.Id, request.SyncSettings);
        }
      }

      // Start sync
      var userGrain = await _grainFactory.GetGrain<IUserActor>(userInfo.Id);
      userGrain.Sync().LogFailure($"{userInfo.Login} ({userInfo.Id})");

      return Ok(userInfo);
    }

    private async Task<(g.Account UserInfo, IHttpActionResult ErrorResult)> LoginCommon(string token, CancellationToken cancellationToken) {
      // This would really be a great place for F# discriminated unions.
      // Return the user info or an error.

      var userResponse = await GitHubUser(token, cancellationToken);

      if (!userResponse.IsOk) {
        return (UserInfo: null, ErrorResult: Error("Unable to determine account from token.", HttpStatusCode.InternalServerError, userResponse.Error));
      }

      var userInfo = userResponse.Result;
      if (userInfo.Type != g.GitHubAccountType.User) {
        return (UserInfo: null, ErrorResult: Error("Token must be for a user.", HttpStatusCode.BadRequest));
      }

      // Check scopes (currently a duplicate check here, I know).
      var scopesOk = _validScopesCollection.Any(x => x.IsSubsetOf(userResponse.Scopes));
      if (!scopesOk) {
        return (
          UserInfo: null,
          ErrorResult: Error("Insufficient scopes granted.", HttpStatusCode.Unauthorized, new {
            Granted = userResponse.Scopes,
          })
        );
      }

      using (var context = new d.ShipHubContext()) {
        // Create account using stored procedure
        // This ensures it exists (simpler logic) and also won't collide with sync.
        await context.BulkUpdateAccounts(userResponse.Date, new[] { _mapper.Map<AccountTableType>(userInfo) });

        // Save user access details
        await context.SetUserAccessToken(userInfo.Id, string.Join(",", userResponse.Scopes), userResponse.RateLimit);
      }

      return (UserInfo: userInfo, ErrorResult: null);
    }
  }
}
