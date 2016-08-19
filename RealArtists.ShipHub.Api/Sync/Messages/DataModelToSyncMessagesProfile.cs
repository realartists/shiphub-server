namespace RealArtists.ShipHub.Api.Sync.Messages {
  using AutoMapper;
  using AutoMapper.Configuration.Conventions;
  using Common.DataModel;
  using Common.DataModel.Types;

  public class DataModelToApiModelProfile : Profile {
    public DataModelToApiModelProfile() {
      // Ensures internal use of "Id" maps to external use of "Identifier"
      // This is gross, but sooooooo easy. Worth it.
      AddMemberConfiguration()
        .AddMember<NameSplitMember>()
        .AddName<PrePostfixName>(_ => _.AddStrings(p => p.DestinationPostfixes, "entifier"));

      CreateMap<Comment, CommentTableType>(MemberList.Destination);
    }
  }
}
