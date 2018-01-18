namespace RealArtists.ShipHub.ActorInterfaces {
  using System;
  using System.Threading.Tasks;
  using Common.GitHub.Models.WebhookPayloads;
  using Orleans;
  using Orleans.CodeGeneration;

  [Version(Constants.InterfaceBaseVersion + 2)]
  public interface IWebhookEventActor : IGrainWithIntegerKey {
    Task CommitComment(DateTimeOffset eventDate, CommitCommentPayload payload);
    Task Installation(DateTimeOffset eventDate, InstallationPayload payload);
    Task InstallationRepositories(DateTimeOffset eventDate, InstallationRepositoriesPayload payload);
    Task IssueComment(DateTimeOffset eventDate, IssueCommentPayload payload);
    Task Issues(DateTimeOffset eventDate, IssuesPayload payload);
    Task Label(DateTimeOffset eventDate, LabelPayload payload);
    Task Milestone(DateTimeOffset eventDate, MilestonePayload payload);
    Task PullRequestReviewComment(DateTimeOffset eventDate, PullRequestReviewCommentPayload payload);
    Task PullRequestReview(DateTimeOffset eventDate, PullRequestReviewPayload payload);
    Task PullRequest(DateTimeOffset eventDate, PullRequestPayload payload);
    Task Push(DateTimeOffset eventDate, PushPayload payload);
    Task Repository(DateTimeOffset eventDate, RepositoryPayload payload);
    Task Status(DateTimeOffset eventDate, StatusPayload payload);
  }
}
