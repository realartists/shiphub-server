namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;

  /// <summary>
  /// These operations only make sense for repository administrators. 
  /// </summary>
  public interface IGitHubRepositoryAdmin {
    Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, IEnumerable<string> events, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }
}
