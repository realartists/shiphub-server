namespace RealArtists.ShipHub.Common.GitHub.Models {
  public enum EventType {
    CommitCommentEvent,
    CreateEvent,
    DeleteEvent,
    DeploymentEvent,
    DeploymentStatusEvent,
    DownloadEvent,
    FollowEvent,
    ForkEvent,
    ForkApplyEvent,
    GistEvent,
    GollumEvent,
    IssueCommentEvent,
    IssuesEvent,
    MemberEvent,
    MembershipEvent,
    PageBuildEvent,
    PublicEvent,
    PullRequestEvent,
    PullRequestReviewCommentEvent,
    PushEvent,
    ReleaseEvent,
    RepositoryEvent,
    StatusEvent,
    TeamAddEvent,
    WatchEvent,
  }

  public class WebhookEvent {
  }
}
