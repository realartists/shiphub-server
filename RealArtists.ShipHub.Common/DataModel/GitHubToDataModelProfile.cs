namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using AutoMapper;
  using Types;
  using g = GitHub.Models;

  public class GitHubToDataModelProfile : Profile {
    protected override void Configure() {
      CreateMap<g.Account, Account>(MemberList.Source)
        .ForSourceMember(x => x.Type, o => o.Ignore())
        .BeforeMap((from, to) => {
          if (from.Id != to.Id) {
            throw new InvalidOperationException($"Cannot update Account {to.Id} with data from GitHub Account {from.Id}");
          }
        });

      CreateMap<g.Repository, Repository>(MemberList.Source)
        .ForSourceMember(x => x.HasIssues, o => o.Ignore())
        .ForSourceMember(x => x.UpdatedAt, o => o.Ignore())
        .ForMember(x => x.AccountId, o => o.MapFrom(x => x.Owner.Id));

      // Table Types
      CreateMap<g.Account, AccountTableType>(MemberList.Destination)
        .ForMember(x => x.Type, o => o.ResolveUsing(x => x.Type == g.GitHubAccountType.User ? Account.UserType : Account.OrganizationType));

      CreateMap<g.Repository, RepositoryTableType>(MemberList.Destination)
        .ForMember(x => x.AccountId, o => o.MapFrom(x => x.Owner.Id));
    }
  }
}