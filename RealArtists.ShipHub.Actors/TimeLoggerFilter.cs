namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Diagnostics;
  using System.Reflection;
  using System.Threading.Tasks;
  using Orleans;
  using RealArtists.ShipHub.Common;

  public class TimeLoggerFilter : IGrainCallFilter {
    private IShipHubRuntimeConfiguration _runtimeConfiguration;

    public TimeLoggerFilter(IShipHubRuntimeConfiguration runtimeConfiguration) {
      _runtimeConfiguration = runtimeConfiguration;
    }

    public async Task Invoke(IGrainCallContext context) {
      var timerInfo = context.Method.GetCustomAttribute<LogElapsedTimeAttribute>();
      var globalMillis = _runtimeConfiguration.LogGrainCallsExceedingMilliseconds;

      if (context.Grain is IGrain grain && (timerInfo != null || globalMillis > 0)) {
        var sw = Stopwatch.StartNew();
        var logMillis = timerInfo?.IfExceedsMilliseconds ?? globalMillis;

        try {
          await context.Invoke();
        } finally {
          sw.Stop();
          if (sw.ElapsedMilliseconds > logMillis) {
            grain.Info($"GRAIN_ELAPSED_TIME {grain.GetType().Name}.{context.Method.Name}: {sw.Elapsed}", "", "", 0);
          }
        }
      } else {
        await context.Invoke();
      }
    }
  }

  [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
  public sealed class LogElapsedTimeAttribute : Attribute {
    public long IfExceedsMilliseconds { get; set; } = -1;
  }
}
