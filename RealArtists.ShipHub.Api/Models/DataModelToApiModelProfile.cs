namespace RealArtists.ShipHub.Api.Models {
  using System;
  using AutoMapper;
  using DataModel;

  public class DataModelToApiModelProfile : Profile {
    protected override void Configure() {
      // Ensures internal use of "Id" maps to external use of "Identifier"
      // This is gross, but sooooooo easy. Worth it.
      AddMemberConfiguration()
        .AddMember<NameSplitMember>()
        .AddName<PrePostfixName>(_ => _.AddStrings(p => p.DestinationPostfixes, "entifier"));

      CreateMap<User, ApiUser>(MemberList.Destination)
        .ForMember(x => x.Type, opts => opts.UseValue(ApiAccountType.User));

    }
  }
}