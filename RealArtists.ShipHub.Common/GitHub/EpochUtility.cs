namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public class EpochUtility {
    public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTimeOffset EpochOffset = new DateTimeOffset(Epoch);

    public static DateTime ToDateTime(int seconds) {
      return Epoch.AddSeconds(seconds);
    }

    public static DateTime ToDateTime(double seconds) {
      return Epoch.AddSeconds(seconds);
    }

    public static double ToEpoch(DateTime value) {
      return value.ToUniversalTime().Subtract(Epoch).TotalSeconds;
    }

    public static DateTimeOffset ToDateTimeOffset(int seconds) {
      return EpochOffset.AddSeconds(seconds);
    }

    public static DateTimeOffset ToDateTimeOffset(double seconds) {
      return EpochOffset.AddSeconds(seconds);
    }

    public static double ToEpoch(DateTimeOffset value) {
      return value.ToUniversalTime().Subtract(EpochOffset).TotalSeconds;
    }
  }
}
