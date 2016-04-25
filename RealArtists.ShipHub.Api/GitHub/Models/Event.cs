namespace RealArtists.ShipHub.Api.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;

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

  public class Event : GitHubModel {
  }
}
