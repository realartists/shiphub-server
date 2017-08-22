namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Diagnostics;
  using System.Reflection;
  using System.Threading.Tasks;
  using Orleans;

  public class TimeLoggerFilter : IGrainCallFilter {
    public async Task Invoke(IGrainCallContext context) {
      var timerInfo = context.Method.GetCustomAttribute<LogElapsedTimeAttribute>();

      if (context.Grain is IGrain grain && timerInfo != null) {
        var sw = Stopwatch.StartNew();

        try {
          await context.Invoke();
        } finally {
          sw.Stop();
          if (sw.ElapsedMilliseconds > timerInfo.IfExceedsMilliseconds) {
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
