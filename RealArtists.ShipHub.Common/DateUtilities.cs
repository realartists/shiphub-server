namespace RealArtists.ShipHub.Common {
  using System;

  public static class DateUtilities {
    public static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right) {
      if (left > right) {
        return left;
      } else if (right > left) {
        return right;
      } else {
        // Whichever one is not null, or null.
        return left ?? right;
      }
    }
  }
}
