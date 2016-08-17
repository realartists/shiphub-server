namespace RealArtists.ShipHub.Api {
  using System;
  using AutoMapper;
  using Common.DataModel;
  using Sync.Messages;

  public static class AutoMapperConfig {
    [ThreadStatic]
    static IMapper _mapper;

    public static IMapper Mapper {
      get {
        if (_mapper == null) {
          var config = new MapperConfiguration(cfg => {
            cfg.AddProfile<GitHubToDataModelProfile>();
            cfg.AddProfile<DataModelToApiModelProfile>();
          });
          _mapper = config.CreateMapper();
        }
        return _mapper;
      }
    }
  }
}