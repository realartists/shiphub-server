namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Text;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Microsoft.Azure.WebJobs;
  using Newtonsoft.Json.Linq;
  using RealArtists.ShipHub.Common.DataModel.Types;
  using RealArtists.ShipHub.Common.GitHub;
  using RealArtists.ShipHub.Common.GitHub.Models;
  using RealArtists.ShipHub.QueueClient;
  using RealArtists.ShipHub.QueueClient.Messages;
  using Tracing;

  public class RollTokens : LoggingHandlerBase {
    private const int TokenVersion = 1;

    private IShipHubConfiguration _config;

    public RollTokens(IShipHubConfiguration config, IDetailedExceptionLogger logger)
      : base(logger) {
      _config = config;
    }

    [Singleton]
    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "timerInfo")]
    public async Task TokenRollTimer(
      [TimerTrigger("0 */10 * * * *")] TimerInfo timerInfo,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, null, null, async () => {
        await Run(notifyChanges);
      });
    }

    public async Task Run(IAsyncCollector<ChangeMessage> notifyChanges) {
      using (var context = new ShipHubContext()) {
        // Get all the tokens
        var tokens = await context.Tokens
          .AsNoTracking()
          .Where(x => x.Version < TokenVersion)
          .ToArrayAsync();

        if (tokens.Any()) {
          Log.Info($"{tokens.Length} tokens need to be rolled.");

          foreach (var token in tokens) {
            var speedLimit = Task.Delay(1000);
            try {
              var newToken = await ResetToken(token.Token);

              if (newToken == null) {
                // Delete the single token
                await context.DeleteUserAccessToken(token.UserId, token.Token);
                Log.Info("Deleted expired token.");
              } else {
                // Replace the token
                await context.RollUserAccessToken(token.UserId, token.Token, newToken, TokenVersion);
                Log.Info("Updated valid token.");
              }

              var cs = new ChangeSummary();
              cs.Add(userId: token.UserId);
              await notifyChanges.AddAsync(new ChangeMessage(cs));

            } catch (Exception e) {
              Log.Exception(e, $"Error rolling token for {token.UserId}:{token.Version}");
            }

            await speedLimit;
          }

          Log.Info($"Done processing tokens.");
        }
      }
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

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private async Task<string> ResetToken(string accessToken) {
      var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_config.GitHubApiRoot, $"applications/{_config.GitHubClientId}/tokens/{accessToken}"));
      httpRequest.Headers.Accept.Clear();
      httpRequest.Headers.Accept.ParseAdd("application/json");

      var response = await _AppClient.SendAsync(httpRequest);

      if (response.IsSuccessStatusCode) {
        var temp = await response.Content.ReadAsAsync<JToken>(GitHubSerialization.MediaTypeFormatters);
        if (temp["error"] != null) {
          throw new Exception(temp.ToString());
        } else {
          var result = temp.ToObject<ResetAccessToken>(GitHubSerialization.JsonSerializer);
          return result.Token;
        }
      } else if (response.StatusCode == HttpStatusCode.NotFound) {
        return null;
      } else {
        throw new Exception($"Failed to roll token {response.StatusCode}");
      }
    }
  }
}

