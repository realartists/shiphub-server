namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.Threading;
  using System.Threading.Tasks;
  using RealArtists.ShipHub.Common.DataModel;

  public interface IShipHubRuntimeConfiguration {
    int GitHubMaxConcurrentRequestsPerUser { get; }
    bool GitHubPaginationInterpolationEnabled { get; }
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

    private int _GitHubMaxConcurrentRequestsPerUser;
    public int GitHubMaxConcurrentRequestsPerUser => _GitHubMaxConcurrentRequestsPerUser;

    private int _GitHubPaginationInterpolationEnabled;
    public bool GitHubPaginationInterpolationEnabled => _GitHubPaginationInterpolationEnabled != 0;

    private int _CommentSpiderEnabled;
    public bool CommentSpiderEnabled => _CommentSpiderEnabled != 0;

    private int _LogGrainCallsExceedingMilliseconds;
    public int LogGrainCallsExceedingMilliseconds => _LogGrainCallsExceedingMilliseconds;

    private void ProcessSettings(IDictionary<string, string> settings = null) {
      var setting = settings?.Val("GitHubMaxConcurrentRequestsPerUser") ?? "2";
      if (!int.TryParse(setting, out var newA)) {
        Log.Info($"Invalid value {setting} for {nameof(GitHubMaxConcurrentRequestsPerUser)}");
      }
      var oldA = Interlocked.Exchange(ref _GitHubMaxConcurrentRequestsPerUser, newA);
      if (oldA != newA) {
        Log.Info($"{nameof(GitHubMaxConcurrentRequestsPerUser)} changed. {oldA} => {newA}");
      }

      setting = settings?.Val("GitHubPaginationInterpolationEnabled") ?? "0";
      if (!int.TryParse(setting, out var newB)) {
        Log.Info($"Invalid value {setting} for {nameof(GitHubPaginationInterpolationEnabled)}");
      }
      var oldB = Interlocked.Exchange(ref _GitHubPaginationInterpolationEnabled, newB);
      if (oldB != newB) {
        Log.Info($"{nameof(GitHubPaginationInterpolationEnabled)} changed. {oldB} => {newB}");
      }

      setting = settings?.Val("CommentSpiderEnabled") ?? "0";
      if (!int.TryParse(setting, out var newC)) {
        Log.Info($"Invalid value {setting} for {nameof(CommentSpiderEnabled)}");
      }
      var oldC = Interlocked.Exchange(ref _CommentSpiderEnabled, newC);
      if (oldC != newC) {
        Log.Info($"{nameof(CommentSpiderEnabled)} changed. {oldC} => {newC}");
      }

      setting = settings?.Val("LogGrainCallsExceedingMilliseconds") ?? "0";
      if (int.TryParse(setting, out var newD)) {
        Log.Info($"Invalid value {setting} for {nameof(LogGrainCallsExceedingMilliseconds)}");
      }
      var oldD = Interlocked.Exchange(ref _LogGrainCallsExceedingMilliseconds, newD);
      if (oldD != newD) {
        Log.Info($"{nameof(LogGrainCallsExceedingMilliseconds)} changed. {oldD} => {newD}");
      }
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
