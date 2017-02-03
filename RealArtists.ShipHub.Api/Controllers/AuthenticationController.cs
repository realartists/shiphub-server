namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Data.Entity;
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
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Orleans;

  public class LoginRequest {
    public string AccessToken { get; set; }
    public string ClientName { get; set; }
  }

  [AllowAnonymous]
  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubController, IGitHubClient {
    private static readonly IGitHubHandler _handlerPipeline = new GitHubHandler();

    private IGrainFactory _grainFactory;
    private IMapper _mapper;

    private static readonly IReadOnlyList<ImmutableHashSet<string>> _validScopesCollection = new List<ImmutableHashSet<string>>() {
      ImmutableHashSet.Create(
        "admin:org_hook",
        "admin:repo_hook",
        "read:org",
        "repo",
        "user:email"),
      ImmutableHashSet.Create(
        "admin:org_hook",
        "admin:repo_hook",
        "notifications",
        "public_repo",
        "read:org",
        "user:email"),
    }.AsReadOnly();

    public AuthenticationController(IGrainFactory grainFactory, IMapper mapper) {
      _grainFactory = grainFactory;
      _mapper = mapper;
    }

    // ///////////////////////////////////////////////////
    // Gross hack until I can fix UserActor to use tokens
    // ///////////////////////////////////////////////////

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
    public Uri ApiRoot { get; } = ShipHubCloudConfiguration.Instance.GitHubApiRoot;
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public long UserId { get; } = -1;
    public string UserInfo { get; } = "ShipHub Authentication Controller (-1)";

    public string AccessToken { get; private set; }

    public int NextRequestId() {
      return 1;
    }

    public Task<GitHubResponse<Common.GitHub.Models.Account>> GitHubUser(IGitHubHandler handler, string accessToken, CancellationToken cancellationToken) {
      AccessToken = accessToken;
      var request = new GitHubRequest("user");
      return handler.Fetch<Common.GitHub.Models.Account>(this, request, cancellationToken);
    }

    // ///////////////////////////////////////////////////
    // For grant revocation
    // ///////////////////////////////////////////////////

    private static HttpClient _BasicClient = new HttpClient(HttpUtilities.CreateDefaultHandler());
    private static readonly string _GitHubClientId = ShipHubCloudConfiguration.Instance.GitHubClientId;
    private static readonly string _GitHubClientSecret = ShipHubCloudConfiguration.Instance.GitHubClientSecret;

    public async Task<bool> RevokeGrant(string accessToken) {
      var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(ApiRoot, $"applications/{_GitHubClientId}/grants/{accessToken}"));
      request.Headers.Authorization = new AuthenticationHeaderValue(
        "basic",
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_GitHubClientId}:{_GitHubClientSecret}"))
      );
      request.Headers.UserAgent.Clear();
      request.Headers.UserAgent.Add(UserAgent);
      var response = await _BasicClient.SendAsync(request);
      return response.StatusCode == HttpStatusCode.NoContent;
    }

    // ///////////////////////////////////////////////////
    // Web calls
    // ///////////////////////////////////////////////////

    [HttpDelete]
    [Authorize]
    [Route("login")]
    public async Task<IHttpActionResult> Logout() {
      // User wants to log out.
      using (var context = new ShipHubContext()) {
        var hookDetails = await context.GetLogoutWebhooks(ShipHubUser.UserId);
        var github = _grainFactory.GetGrain<IGitHubActor>(ShipHubUser.UserId);
        var tasks = new List<Task>();

        // Delete all repo hooks where they're the only user
        tasks.AddRange(hookDetails.RepositoryHooks.Select(x => github.DeleteRepositoryWebhook(x.Name, x.HookId)));
        // Delete all org hooks where they're the only user
        tasks.AddRange(hookDetails.OrganizationHooks.Select(x => github.DeleteOrganizationWebhook(x.Name, x.HookId)));

        // Wait and log errors.
        string userInfo = $"{ShipHubUser.Login} ({ShipHubUser.UserId})";
        try {
          // Ensure requests complete before we revoke our access below
          await Task.WhenAll(tasks);
          foreach (var task in tasks) {
            task.LogFailure(userInfo);
          }
        } catch {
          // They're logging out. We had our chance.
        }

        RevokeGrant(ShipHubUser.Token).LogFailure(userInfo);

        // Invalidate their token with ShipHub
        await context.RevokeAccessToken(ShipHubUser.Token);
      }

      return Ok();
    }

    [HttpPost]
    [Route("login")]
    public async Task<IHttpActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken) {
      if ((request?.AccessToken).IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.AccessToken)} is required.");
      }
      if (request.ClientName.IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      var userResponse = await GitHubUser(_handlerPipeline, request.AccessToken, cancellationToken);

      if (!userResponse.IsOk) {
        Error("Unable to determine account from token.", HttpStatusCode.InternalServerError, userResponse.Error);
      }

      // Check scopes
      bool scopesOk = _validScopesCollection.Any(x => x.IsSubsetOf(userResponse.Scopes));
      if (!scopesOk) {
        return Error("Insufficient scopes granted.", HttpStatusCode.Unauthorized, new {
          Granted = userResponse.Scopes,
        });
      }

      var userInfo = userResponse.Result;

      if (userInfo.Type != Common.GitHub.Models.GitHubAccountType.User) {
        Error("Token must be for a user.", HttpStatusCode.BadRequest);
      }

      using (var context = new ShipHubContext()) {
        // Create account using stored procedure
        // This ensures it exists (simpler logic) and also won't collide with sync.
        await context.BulkUpdateAccounts(userResponse.Date, new[] { _mapper.Map<AccountTableType>(userInfo) });

        // Save user access details
        await context.SetUserAccessToken(userInfo.Id, string.Join(",", userResponse.Scopes), userResponse.RateLimit);
      }

      var userGrain = _grainFactory.GetGrain<IUserActor>(userInfo.Id);
      userGrain.Sync().LogFailure($"{userInfo.Login} ({userInfo.Id})");

      return Ok(userInfo);
    }
  }
}
