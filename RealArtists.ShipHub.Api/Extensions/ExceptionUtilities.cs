namespace RealArtists.ShipHub.Api {
  using System;
  using System.Linq;

  public static class ExceptionUtilities {
    public static Exception Simplify(this Exception e) {
      var agg = e as AggregateException;

      if (agg != null) {
        agg = agg.Flatten();

        if (agg.InnerExceptions.Count == 1) {
          return Simplify(agg.InnerExceptions.Single());
        }
      }

      return e;
    }
  }
}