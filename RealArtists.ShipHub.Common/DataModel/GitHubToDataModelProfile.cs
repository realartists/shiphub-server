namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Diagnostics;
  using AutoMapper;
  using Newtonsoft.Json;
  using Types;
  using g = GitHub.Models;

  public class GitHubToDataModelProfile : Profile {
    public GitHubToDataModelProfile() {
      CreateMap<g.Account, Account>(MemberList.Destination);

      // Table Types
      CreateMap<g.Account, AccountTableType>(MemberList.Destination)
        .ForMember(x => x.Type, o => o.ResolveUsing(x => {
          switch (x.Type) {
            case g.GitHubAccountType.Organization:
              return Account.OrganizationType;
            case g.GitHubAccountType.User:
            case g.GitHubAccountType.Bot:
              return Account.UserType;
            default:
              Log.Error("Mapping untyped account: " + Environment.StackTrace);
              Debug.Assert(false, "Un-typed account");
              return Account.UserType;
          }
        }));

      CreateMap<g.Comment, CommentTableType>(MemberList.Destination)
        .BeforeMap((from, to) => {
          if (from.IssueNumber == null) {
            throw new InvalidOperationException("Only issue comments are supported.");
          }
        });

      CreateMap<g.Issue, IssueTableType>(MemberList.Destination)
        .ForMember(x => x.PullRequest, o => o.ResolveUsing(x => x.PullRequest != null))
        .ForMember(x => x.Reactions, o => o.ResolveUsing(x => x.Reactions.SerializeObject(Formatting.None)));

      CreateMap<g.IssueEvent, IssueEventTableType>(MemberList.Destination);

      CreateMap<g.Milestone, MilestoneTableType>(MemberList.Destination);

      CreateMap<g.Project, ProjectTableType>(MemberList.Destination)
        .ForMember(x => x.CreatorId, o => o.MapFrom(x => x.Creator.Id));

      CreateMap<g.Repository, RepositoryTableType>(MemberList.Destination)
        .ForMember(x => x.AccountId, o => o.MapFrom(x => x.Owner.Id))
        .ForMember(x => x.Disabled, o => o.UseValue<bool?>(null));

      CreateMap<g.Reaction, ReactionTableType>(MemberList.Destination);
    }
  }
}
