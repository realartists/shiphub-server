namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Diagnostics;
  using System.IdentityModel.Tokens.Jwt;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Security.Claims;
  using System.Security.Cryptography;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Microsoft.IdentityModel.Tokens;
  using Orleans;
  using Orleans.Concurrency;

  // For now. this won't be re-entrant.
  // This has the advantage of serializing requests to GitHub as well so we (hopefully) aren't limited.
  // Also, these requests return no rate limit headers.
  [StatelessWorker]
  public class GitHubAppActor : Grain, IGitHubAppActor, IGitHubClient {
    private const string GitHubAppAccept = "application/vnd.github.machine-man-preview+json";

    private const double JWTValidMinutes = 9.5; // Not 10 to allow for clock drift between us and GitHub.

    // Should be less than Orleans timeout.
    // If changing, may also need to update values in CreateGitHubHttpClient()
    public static readonly TimeSpan GitHubRequestTimeout = OrleansAzureClient.ResponseTimeout.Subtract(TimeSpan.FromSeconds(2));

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private IShipHubConfiguration _configuration;

    public Uri ApiRoot { get; }
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public string UserInfo => $"GitHubApp: {_configuration.GitHubAppId}";
    public long UserId { get; } = -2;

    private object _jwtLock = new object();
    private DateTimeOffset _jwtExpires;
    private string _jwt;
    public string AccessToken {
      get {
        lock (_jwtLock) {
          if (string.IsNullOrWhiteSpace(_jwt) || DateTimeOffset.UtcNow > _jwtExpires) {
            using (var rsa = new RSACryptoServiceProvider()) {
              var csp64 = _configuration.GitHubAppSigningKey;
              var csp = Convert.FromBase64String(csp64);
              rsa.ImportCspBlob(csp);

              var key = new RsaSecurityKey(rsa);
              var creds = new SigningCredentials(key, "RS256");
              var jwt = new JwtSecurityTokenHandler() {
                SetDefaultTimesOnTokenCreation = true,
                TokenLifetimeInMinutes = 10,
              };

              var expiration = DateTime.UtcNow.AddMinutes(JWTValidMinutes);
              var header = new JwtHeader(creds);
              var payload = new JwtPayload(
                _configuration.GitHubAppId,
                null,
                new[] { new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer) },
                null,
                expiration);

              var token = new JwtSecurityToken(header, payload);

              var tokenString = jwt.WriteToken(token);

              // IFF all this worked, update the class vars
              _jwtExpires = expiration;
              _jwt = tokenString;
            }
          }
        }
        return _jwt;
      }
    }

    private static GitHubHandler SharedHandler;
    private static void EnsureHandlerPipelineCreated(Uri apiRoot) {
      if (SharedHandler != null) {
        return;
      }

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointConnectionLimit(apiRoot);

      SharedHandler = new GitHubHandler();
    }

    public GitHubAppActor(IShipHubConfiguration configuration) {
      _configuration = configuration;

      ApiRoot = _configuration.GitHubApiRoot;
      EnsureHandlerPipelineCreated(ApiRoot);
    }

    ////////////////////////////////////////////////////////////
    // Helpers
    ////////////////////////////////////////////////////////////

    private int _requestId = 0;
    public int NextRequestId() {
      return Interlocked.Increment(ref _requestId);
    }

    ////////////////////////////////////////////////////////////
    // GitHub Actions
    ////////////////////////////////////////////////////////////

    public Task<GitHubResponse<App>> App(RequestPriority priority = RequestPriority.Background) {
      var request = new GitHubRequest(HttpMethod.Get, $"app", priority) {
        AcceptHeaderOverride = GitHubAppAccept,
      };
      return Fetch<App>(request);
    }

    public Task<GitHubResponse<TimedToken>> CreateInstallationToken(long installationId, RequestPriority priority = RequestPriority.Background) {
      // Don't cache here - let the installation actor manage it.
      var request = new GitHubRequest(HttpMethod.Post, $"installations/{installationId}/access_tokens", priority) {
        AcceptHeaderOverride = GitHubAppAccept,
      };
      return Fetch<TimedToken>(request);
    }

    ////////////////////////////////////////////////////////////
    // HTTP Helpers
    ////////////////////////////////////////////////////////////

    private Task<GitHubResponse<T>> Fetch<T>(GitHubRequest request) {
      var totalWait = Stopwatch.StartNew();
      try {
        using (var timeout = new CancellationTokenSource(GitHubRequestTimeout)) {
          return SharedHandler.Fetch<T>(this, request, timeout.Token);
        }
      } catch (TaskCanceledException exception) {
        totalWait.Stop();
        exception.Report($"GitHub Request Timeout after {totalWait.ElapsedMilliseconds}ms for [{request.Uri}]");
        throw;
      }
    }
  }
}
