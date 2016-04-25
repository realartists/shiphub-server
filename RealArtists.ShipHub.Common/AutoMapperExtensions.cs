namespace RealArtists.ShipHub.Common {
  using AutoMapper;

  public static class AutoMapperExtensions {
    public static IMappingExpression<TSource, TDestination> IgnoreAll<TSource, TDestination>(this IMappingExpression<TSource, TDestination> mapping) {
      mapping.ForAllMembers(opts => opts.Ignore());
      return mapping;
    }
  }
}
