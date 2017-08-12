namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;

  /// <summary>
  /// These operations only make sense for organization administrators. 
  /// </summary>
  public interface IGitHubOrganizationAdmin {
    Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, IEnumerable<string> events, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }
}
