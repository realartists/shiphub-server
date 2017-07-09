namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Threading;
  using System.Threading.Tasks;
  using Newtonsoft.Json;
  using RealArtists.ShipHub.Common.GitHub;

  public static class StatHat {
    private static readonly string StatHatApiKey = ShipHubCloudConfiguration.Instance.StatHatKey;
    private static readonly string StatHatPrefix = ShipHubCloudConfiguration.Instance.StatHatPrefix;

    // /////////////////////////////////////////////////////////////
    // Counter Stats
    // /////////////////////////////////////////////////////////////
    public static void Count(string statName) {
      Record(statName, 1, null, DateTimeOffset.UtcNow);
    }

    public static void Count(string statName, long number) {
      Record(statName, number, null, DateTimeOffset.UtcNow);
    }

    public static void Count(string statName, DateTimeOffset timestamp) {
      Record(statName, 1, null, timestamp);
    }

    public static void Count(string statName, long number, DateTimeOffset timestamp) {
      Record(statName, number, null, timestamp);
    }

    // /////////////////////////////////////////////////////////////
    // Value Stats
    // /////////////////////////////////////////////////////////////

    public static void Value(string statName, double current) {
      Record(statName, null, current, DateTimeOffset.UtcNow);
    }

    public static void Value(string statName, double current, DateTimeOffset timestamp) {
      Record(statName, null, current, timestamp);
    }

    // /////////////////////////////////////////////////////////////
    // Accounting
    // /////////////////////////////////////////////////////////////

    private static void Record(string statName, long? count, double? value, DateTimeOffset timestamp) {
      if (!StatHatPrefix.IsNullOrWhiteSpace()) {
        statName = StatHatPrefix + statName;
      }

#if DEBUG
      Debug.Assert(!statName.IsNullOrWhiteSpace(), $"Invalid StatHat name: '{statName}'");
      Debug.Assert(statName.Length <= 255, $"Invalid StatHat name (too long): '{statName}'");
      Debug.Assert(count.HasValue != value.HasValue, $"Statistics must have a count or a value but not both. ({statName})");
#endif

      if (StatHatApiKey.IsNullOrWhiteSpace()) { return; }
      if (statName.IsNullOrWhiteSpace()) { return; }

      var stat = new StatHatStatistic() {
        Name = statName,
        Count = count,
        Value = value,
        UnixTimestamp = (int)EpochUtility.ToEpoch(timestamp)
      };

      Sink.Record(stat);

#if DEBUG
      WriteStatToConsole(stat);
#endif
    }

#if DEBUG
    private static void WriteStatToConsole(StatHatStatistic stat) {
      var type = stat.Value != null ? "V" : "C";
      object value = stat.Count ?? stat.Value;
      Console.WriteLine($"[STATHAT] [{EpochUtility.ToDateTimeOffset(stat.UnixTimestamp):o}] [{stat.Name} ({type})]: {value}");
    }
#endif

    // /////////////////////////////////////////////////////////////
    // Submission
    // /////////////////////////////////////////////////////////////

    private class StatHatStatistic {
      [JsonProperty("stat")]
      public string Name { get; set; }

      [JsonProperty("count")]
      public long? Count { get; set; }

      [JsonProperty("value")]
      public double? Value { get; set; }

      [JsonProperty("t")]
      public int UnixTimestamp { get; set; }
    }

    private class StatHatRequest {
      [JsonProperty("ezkey")]
      [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
      public string Key { get; set; }

      [JsonProperty("data")]
      [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
      public IEnumerable<StatHatStatistic> Data { get; set; }
    }

    private static Lazy<StatSink> _sink = new Lazy<StatSink>(() => new StatSink());
    private static StatSink Sink => _sink.Value;

    private class StatSink : IDisposable {
      private const long HysteresisMillis = 500; // time we wait to flush when logs are sent slowly
      private const long FlushCount = 1000; // logs will be flushed immediately if more than this amount queue up
      private const long MaxBufferCount = 10000; // max logs to buffer. above this count, we start dropping them.

      private object _lock = new object();
      private int _count = 0;
      private Dictionary<long, Dictionary<string, StatHatStatistic>> _buffer = new Dictionary<long, Dictionary<string, StatHatStatistic>>();

      private Timer _timer;
      private bool _timerScheduled = false;
      private Thread _consumerThread;

      private static readonly Uri StatHatApiRoot = new Uri("https://api.stathat.com/ez");
      private HttpClient _client;

      [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
      public StatSink() {
        // Allow loads of requests to StatHat
        HttpUtilities.SetServicePointConnectionLimit(StatHatApiRoot);

        // NOTE: Very important we not log statistics for calls to StatHat, as the statistics
        // generate calls to StatHat, which would log more statistics, which ...
        _client = new HttpClient(HttpUtilities.CreateDefaultHandler(logStatistics: false), true) {
          Timeout = TimeSpan.FromMinutes(2),
        };

        _timer = new Timer((ctx) => {
          ((StatSink)ctx).TimerFired();
        }, this, Timeout.Infinite, Timeout.Infinite);
        _consumerThread = new Thread(ProcessBuffer) {
          IsBackground = true,
        };
        _consumerThread.Start();
      }

      /* Add a single line to _buffer. Callable from any thread */
      public void Record(StatHatStatistic stat) {
        lock (_lock) {
          if (_count >= MaxBufferCount) { return; }

          var update = _buffer.Valn(stat.UnixTimestamp).Vald(stat.Name, () => stat);

          if (ReferenceEquals(update, stat) == false) {
            if (stat.Value.HasValue) {
              update.Count += stat.Count;
            } else {
              // Replace (old) value
              update.Value = stat.Value;
            }
          } else {
            // we added a new stat
            ++_count;
          }

          if (_count >= FlushCount) {
            if (_timerScheduled) {
              _timerScheduled = false;
              _timer.Change(Timeout.Infinite, Timeout.Infinite); // cancel the timer
            }
            Monitor.Pulse(_lock);
          } else if (!_timerScheduled) {
            _timerScheduled = true;
            _timer.Change(HysteresisMillis, Timeout.Infinite);
          } // else timer will get it later
        }
      }

      private void TimerFired() {
        lock (_lock) {
          _timerScheduled = false;
          Monitor.Pulse(_lock);
        }
      }

      // Runs on a dedicated thread, serially processing buffer
      private void ProcessBuffer() {
        do {
          StatHatStatistic[] stats = null;
          lock (_lock) {
            if (_count == 0) {
              Monitor.Wait(_lock);
              if (_count == 0) {
                continue; // skip spurious wakeup
              }
            }

            stats = _buffer.Values.SelectMany(x => x.Values).ToArray();
            _buffer.Clear();
            _count = 0;
          }

          ReportStats(stats).GetAwaiter().GetResult();

        } while (true);
      }

      private async Task ReportStats(StatHatStatistic[] stats) {
        try {
          var body = new StatHatRequest() {
            Key = StatHatApiKey,
            Data = stats,
          };

          var request = new HttpRequestMessage(HttpMethod.Post, StatHatApiRoot) {
            Content = new ObjectContent<StatHatRequest>(body, JsonMediaTypeFormatter),
          };

          var response = await _client.SendAsync(request);
          if (!response.IsSuccessStatusCode) {
            var errorMessage = $"StatHat Error [{response.StatusCode}]";
            if (response.Content != null) {
              var error = await response.Content.ReadAsStringAsync();
              errorMessage += $": {error}";
            }
            Log.Error(errorMessage);
          }
        } catch (Exception e) {
          Log.Exception(e);
        }
      }

      private bool disposedValue = false; // To detect redundant calls
      protected virtual void Dispose(bool disposing) {
        if (!disposedValue) {
          if (disposing) {
            if (_timer != null) {
              _timer.Dispose();
              _timer = null;
            }

            if (_consumerThread?.IsAlive == true) {
              _consumerThread.Abort();
              _consumerThread = null;
            }

            if (_client != null) {
              _client.Dispose();
              _client = null;
            }
          }

          _buffer = null;

          disposedValue = true;
        }
      }

      // This code added to correctly implement the disposable pattern.
      public void Dispose() {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(true);
      }
    }

    // /////////////////////////////////////////////////////////////
    // Serialization
    // /////////////////////////////////////////////////////////////

    public static JsonSerializerSettings JsonSerializerSettings { get; } = CreateSaneDefaultSettings();
    public static JsonSerializer JsonSerializer { get; } = JsonSerializer.Create(JsonSerializerSettings);

    public static JsonMediaTypeFormatter JsonMediaTypeFormatter { get; } = new JsonMediaTypeFormatter() { SerializerSettings = JsonSerializerSettings };
    public static IEnumerable<MediaTypeFormatter> MediaTypeFormatters { get; } = new[] { JsonMediaTypeFormatter };

    public static JsonSerializerSettings CreateSaneDefaultSettings() {
      var settings = new JsonSerializerSettings() {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
      };

      return settings;
    }
  }
}
