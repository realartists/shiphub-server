namespace RealArtists.ShipHub.Api.Sync.Messages {
  using AutoMapper;
  using Common.DataModel;
  using SyncMessages.Entries;

  public class DataModelToApiModelProfile : Profile {
    protected override void Configure() {
      // Ensures internal use of "Id" maps to external use of "Identifier"
      // This is gross, but sooooooo easy. Worth it.
      AddMemberConfiguration()
        .AddMember<NameSplitMember>()
        .AddName<PrePostfixName>(_ => _.AddStrings(p => p.DestinationPostfixes, "entifier"));

      
    }
  }
}