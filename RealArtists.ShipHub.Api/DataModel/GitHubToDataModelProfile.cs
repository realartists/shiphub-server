namespace RealArtists.ShipHub.Api.DataModel {
  using System;
  using AutoMapper;
  using GitHub = GitHub.Models;

  public class GitHubToDataModelProfile : Profile {
    protected override void Configure() {
      CreateMap<GitHub.Account, Account>(MemberList.Source)
        .ForSourceMember(x => x.Type, opts => opts.Ignore())
        .BeforeMap((from, to) => {
          if (!from.Id.Equals(to.Id)) {
            throw new InvalidOperationException($"Cannot update Account {to.Id} with data from GitHub Account {from.Id}");
          }
        });
    }
  }
}