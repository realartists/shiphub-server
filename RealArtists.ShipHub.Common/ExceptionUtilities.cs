namespace RealArtists.ShipHub.Common {
  using System;
  using System.Linq;

  public static class ExceptionUtilities {
    public static Exception Simplify(this Exception exception) {
      var agg = exception as AggregateException;

      if (agg != null) {
        agg = agg.Flatten();

        if (agg.InnerExceptions.Count == 1) {
          return Simplify(agg.InnerExceptions.Single());
        }
      }

      return exception;
    }
  }
}