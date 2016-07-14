namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public class EpochUtility {
    private static readonly DateTime _Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset _EpochOffset = new DateTimeOffset(_Epoch);

    public static DateTime ToDateTime(int seconds) {
      return _Epoch.AddSeconds(seconds);
    }

    public static DateTime ToDateTime(double seconds) {
      return _Epoch.AddSeconds(seconds);
    }

    public static double ToEpoch(DateTime value) {
      return value.ToUniversalTime().Subtract(_Epoch).TotalSeconds;
    }

    public static DateTimeOffset ToDateTimeOffset(int seconds) {
      return _EpochOffset.AddSeconds(seconds);
    }

    public static DateTimeOffset ToDateTimeOffset(double seconds) {
      return _EpochOffset.AddSeconds(seconds);
    }

    public static double ToEpoch(DateTimeOffset value) {
      return value.ToUniversalTime().Subtract(_EpochOffset).TotalSeconds;
    }

  }
}
