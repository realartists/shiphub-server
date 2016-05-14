namespace GitHubUpdateProcessor {
  using System;
  using AutoMapper;
  using RealArtists.ShipHub.Common.DataModel;

  public static class SharedMapper {
    public static IMapper Mapper { get; private set; }

    static SharedMapper() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
        //cfg.AddProfile<DataModelToApiModelProfile>();
      });
      Mapper = config.CreateMapper();
    }

    public static TDestination Map<TDestination>(object source) {
      return Mapper.Map<TDestination>(source);
    }

    public static TDestination Map<TDestination>(object source, Action<IMappingOperationOptions> opts) {
      return Mapper.Map<TDestination>(source, opts);
    }

    public static TDestination Map<TSource, TDestination>(TSource source) {
      return Mapper.Map<TSource, TDestination>(source);
    }

    public static TDestination Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>> opts) {
      return Mapper.Map<TSource, TDestination>(source, opts);
    }

    public static TDestination Map<TSource, TDestination>(TSource source, TDestination destination) {
      return Mapper.Map<TSource, TDestination>(source, destination);
    }

    public static TDestination Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts) {
      return Mapper.Map<TSource, TDestination>(source, destination, opts);
    }

    public static object Map(object source, Type sourceType, Type destinationType) {
      return Mapper.Map(source, sourceType, destinationType);
    }

    public static object Map(object source, Type sourceType, Type destinationType, Action<IMappingOperationOptions> opts) {
      return Mapper.Map(source, sourceType, destinationType, opts);
    }

    public static object Map(object source, object destination, Type sourceType, Type destinationType) {
      return Mapper.Map(source, destination, sourceType, destinationType);
    }

    public static object Map(object source, object destination, Type sourceType, Type destinationType, Action<IMappingOperationOptions> opts) {
      return Mapper.Map(source, destination, sourceType, destinationType, opts);
    }
  }
}
