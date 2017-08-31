namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.Threading;
  using System.Threading.Tasks;
  using RealArtists.ShipHub.Common.DataModel;

  public interface IShipHubRuntimeConfiguration {
    bool CommentSpiderEnabled { get; }
    int LogGrainCallsExceedingMilliseconds { get; }
  }

  public class ShipHubRuntimeConfiguration : IShipHubRuntimeConfiguration {
    private readonly TimeSpan ValidityPeriod = TimeSpan.FromMinutes(2);

    private IFactory<ShipHubContext> _shipContextFactory;
    [SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
    private Task _timer;

    public ShipHubRuntimeConfiguration(IFactory<ShipHubContext> shipContextFactory) {
      _shipContextFactory = shipContextFactory;
      ProcessSettings();
      _timer = Reload();
    }

    private int _CommentSpiderEnabled;
    public bool CommentSpiderEnabled => _CommentSpiderEnabled != 0;

    private int _LogGrainCallsExceedingMilliseconds;
    public int LogGrainCallsExceedingMilliseconds => _LogGrainCallsExceedingMilliseconds;

    private void ProcessSettings(IDictionary<string, string> settings = null) {
      void UpdateSetting(string name, ref int location, int fallback) {
        var newValue = fallback;

        var setting = settings?.Val(name);
        if (setting != null) {
          if (int.TryParse(setting, out var parsed)) {
            newValue = parsed;
          } else {
            Log.Info($"Invalid value [{setting}] for {name}");
          }
        }

        var oldValue = Interlocked.Exchange(ref location, newValue);
        if (oldValue != newValue) {
          Log.Info($"{name} changed. {oldValue} => {newValue}");
        }
      }

      UpdateSetting("CommentSpiderEnabled", ref _CommentSpiderEnabled, 0);
      UpdateSetting("LogGrainCallsExceedingMilliseconds", ref _LogGrainCallsExceedingMilliseconds, 0);
    }

    private async Task Reload() {
      while (true) {
        try {
          Dictionary<string, string> settings;
          using (var context = _shipContextFactory.CreateInstance()) {
            settings = await context.ApplicationSettings
              .AsNoTracking()
              .ToDictionaryAsync(x => x.Id, x => x.Value)
              .ConfigureAwait(false);
          }

          ProcessSettings(settings);
        } catch (Exception e) {
          e.Report();
        }

        await Task.Delay(ValidityPeriod).ConfigureAwait(false);
      }
    }
  }
}
