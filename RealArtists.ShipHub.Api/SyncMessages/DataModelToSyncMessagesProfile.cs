namespace RealArtists.ShipHub.Api.Models {
  using AutoMapper;
  using AutoMapper.Configuration.Conventions;

  public class DataModelToApiModelProfile : Profile {
    public DataModelToApiModelProfile() {
      // Ensures internal use of "Id" maps to external use of "Identifier"
      // This is gross, but sooooooo easy. Worth it.
      AddMemberConfiguration()
        .AddMember<NameSplitMember>()
        .AddName<PrePostfixName>(_ => _.AddStrings(p => p.DestinationPostfixes, "entifier"));
    }
  }
}
