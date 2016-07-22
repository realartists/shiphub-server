namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using AutoMapper;
  using Newtonsoft.Json;
  using Types;
  using g = GitHub.Models;

  public class GitHubToDataModelProfile : Profile {
    public GitHubToDataModelProfile() {
      CreateMap<g.Account, Account>(MemberList.Destination);

      // Table Types
      CreateMap<g.Account, AccountTableType>(MemberList.Destination)
        .ForMember(x => x.Type, o => o.ResolveUsing(x => x.Type == g.GitHubAccountType.User ? Account.UserType : Account.OrganizationType));

      CreateMap<g.Comment, CommentTableType>(MemberList.Destination)
        .BeforeMap((from, to) => {
          if (from.IssueNumber == null) {
            throw new InvalidOperationException("Only issue comments are supported.");
          }
        }).ForMember(x => x.Reactions, o => o.ResolveUsing(x => x.Reactions.SerializeObject(Formatting.None)));

      CreateMap<g.Issue, IssueTableType>(MemberList.Destination)
        .BeforeMap((from, to) => {
          if (from.PullRequest != null) {
            throw new InvalidOperationException("Pull requests are not supported.");
          }
        }).ForMember(x => x.Reactions, o => o.ResolveUsing(x => x.Reactions.SerializeObject(Formatting.None)));

      CreateMap<g.IssueEvent, IssueEventTableType>(MemberList.Destination)
        .ForMember(x => x.ActorId, o => o.ResolveUsing(x => x.Assigner?.Id ?? x.Actor.Id));

      CreateMap<g.TimelineEvent, IssueEventTableType>(MemberList.Destination)
        .ForMember(x => x.IssueId, o => o.Ignore());

      CreateMap<g.Milestone, MilestoneTableType>(MemberList.Destination);

      CreateMap<g.Repository, RepositoryTableType>(MemberList.Destination)
        .ForMember(x => x.AccountId, o => o.MapFrom(x => x.Owner.Id));
    }
  }
}
