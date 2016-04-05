namespace RealArtists.ShipHub.Api.App_Start {
  using AutoMapper;
  using DataModel;
  using Models;

  public static class AutoMapperConfig {
    public static IMapper Mapper { get; private set; }

    public static void Register() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
        cfg.AddProfile<DataModelToApiModelProfile>();
      });
      Mapper = config.CreateMapper();
    }
  }
}