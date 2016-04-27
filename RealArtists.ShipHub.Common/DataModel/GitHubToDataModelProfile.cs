namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using AutoMapper;
  using g = GitHub.Models;

  public class GitHubToDataModelProfile : Profile {
    protected override void Configure() {
      CreateMap<g.Account, Account>(MemberList.Source)
        .ForSourceMember(x => x.Type, opts => opts.Ignore())
        .BeforeMap((from, to) => {
          if (!from.Id.Equals(to.Id)) {
            throw new InvalidOperationException($"Cannot update Account {to.Id} with data from GitHub Account {from.Id}");
          }
        });

      CreateMap<g.Repository, Repository>(MemberList.Source)
        .ForMember(x => x.AccountId, o => o.MapFrom(x => x.Owner.Id));

      //CreateMap<GH.GitHubResponse, AccessToken>()
      //  .IgnoreAll()
      //  .ForMember(x => x.RateLimit, opts => opts.MapFrom(x => x.RateLimit))
      //  .ForMember(x => x.RateLimitRemaining, opts => opts.MapFrom(x => x.RateLimitRemaining))
      //  .ForMember(x => x.RateLimitReset, opts => opts.MapFrom(x => x.RateLimitReset));

      //CreateMap<GH.GitHubResponse, GitHubMetaData>()
      //  .IgnoreAll()
      //  .BeforeMap((from, to) => {
      //    if (from.Credentials.Parameter != to.AccessToken?.Token) {
      //    }
      //  })
      //  .ForMember(x => x.AccessToken, opts => opts.MapFrom(x => x))
      //  .ForMember(x => x.ETag, opts => opts.MapFrom(x => x.ETag))
      //  .ForMember(x => x.Expires, opts => opts.MapFrom(x => x.Expires))
      //  .ForMember(x => x.LastModified, opts => opts.MapFrom(x => x.LastModified))
      //  .AfterMap((from, to) => {
      //    to.LastRefresh = DateTimeOffset.UtcNow;
      //  });
    }
  }
}