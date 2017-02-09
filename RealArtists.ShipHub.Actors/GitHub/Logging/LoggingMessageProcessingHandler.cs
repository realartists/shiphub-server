namespace RealArtists.ShipHub.Actors.GitHub.Logging {
  using System;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using System.Net.Http;
  using System.Reactive;
  using System.Reactive.Concurrency;
  using System.Reactive.Linq;
  using System.Reactive.Subjects;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Common;
  using Microsoft.WindowsAzure.Storage;
  using Microsoft.WindowsAzure.Storage.Blob;

  public class AppendBlobEntry {
    public string BlobName { get; set; }
    public string Content { get; set; }
  }

  public class LoggingMessageProcessingHandler : DelegatingHandler {
    public const string LogBlobNameKey = "LogBlobName";
    public const string CreationTimeKey = "CreationTime";
    public const string UserInfoKey = "UserInfo";

    private CloudBlobClient _blobClient;
    private string _containerName;

    private Subject<AppendBlobEntry> _appendBlobs;
    private CloudBlobContainer _container;
    private IDisposable _logSubscription;

    public LoggingMessageProcessingHandler(
      CloudStorageAccount storageAccount,
      string containerName,
      HttpMessageHandler innerHandler) : base(innerHandler) {
      if (storageAccount != null) {
        _blobClient = storageAccount.CreateCloudBlobClient();
        _containerName = containerName;
      }
    }

    private bool _initialized = false;
    private SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    public async Task Initialize() {
      await _initLock.WaitAsync();
      try {
        if (_initialized) {
          return; // Race and someone else won.
        }

        if (_blobClient == null) {
          throw new InvalidOperationException("Cannot initialize LoggingMessageProcessingHandler without CloudStorageAccount.");
        }

        _container = _blobClient.GetContainerReference(_containerName);
        await _container.CreateIfNotExistsAsync();

        _appendBlobs = new Subject<AppendBlobEntry>();

        _logSubscription = _appendBlobs
          .ObserveOn(TaskPoolScheduler.Default)
          .Select(entry =>
            Observable.FromAsync(async () => await WriteEntry(entry))
            .Catch<Unit, Exception>(LogError<Unit>))
          .Concat() // Force sequential evaluation
          .Subscribe();

        _initialized = true;
      } finally {
        _initLock.Release();
      }
    }

    private IObservable<T> LogError<T>(Exception exception) {
      exception.Report("Error logging GitHub request.");
      return Observable.Empty<T>();
    }

    protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      var blobName = ExtractString(request, LogBlobNameKey);
      var userInfo = ExtractString(request, UserInfoKey);
      var timer = new Stopwatch();
      HttpResponseMessage response = null;
      Exception logException = null;

      try {
        // Load the request into the buffer so we can copy it later if the request failed.
        request.Content?.LoadIntoBufferAsync();
        timer.Restart();
        response = await base.SendAsync(request, cancellationToken);
        timer.Stop();
      } catch (Exception e) {
        logException = e;
        throw;
      } finally {
        long contentLength = response?.Content?.Headers?.ContentLength ?? 0;
        string logBlob = null;
        string statusLine = response == null ? "FAILED" : $"{(int)response.StatusCode} {response.ReasonPhrase}";

        bool skipLogging = logException != null && logException is TaskCanceledException;
        if (!skipLogging && IsFailure(response) && _blobClient != null && !blobName.IsNullOrWhiteSpace()) {
          logBlob = blobName;

          if (!_initialized) {
            await Initialize();
          }

          using (var ms = new MemoryStream())
          using (var sw = new StreamWriter(ms, Encoding.UTF8)) {
            await WriteRequest(ms, sw, request);

            if (logException != null) {
              sw.WriteLine("\n\nError reading response:\n\n");
              sw.WriteLine(logException.ToString());
            } else {
              await WriteResponse(ms, sw, response);
            }

            _appendBlobs.OnNext(new AppendBlobEntry() {
              BlobName = blobName,
              Content = Encoding.UTF8.GetString(ms.ToArray()),
            });
          }
        }

        Log.Info($"[{userInfo}] {request.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version} - {statusLine} - {timer.ElapsedMilliseconds}ms - {ExtractElapsedTime(request)} - {logBlob} - {contentLength} bytes");
      }

      return response;
    }

    private static bool IsFailure(HttpResponseMessage response) {
      var statusCode = response?.StatusCode;
      return statusCode == null || (int)statusCode < 200 || (int)statusCode >= 400;
    }

    public static void SetLogDetails(HttpRequestMessage request, string userInfo, string blobName, DateTimeOffset creationTime) {
      request.Properties[UserInfoKey] = userInfo;
      request.Properties[LogBlobNameKey] = blobName;
      request.Properties[CreationTimeKey] = creationTime;
    }

    public static string ExtractString(HttpRequestMessage request, string key) {
      string result = null;
      if (request.Properties.ContainsKey(key)) {
        result = request.Properties[key] as string;
      }
      return result;
    }

    public static TimeSpan? ExtractElapsedTime(HttpRequestMessage request) {
      DateTimeOffset? creationTime = null;
      if (request.Properties.ContainsKey(CreationTimeKey)) {
        creationTime = request.Properties[CreationTimeKey] as DateTimeOffset?;
      }

      if (creationTime != null) {
        return DateTimeOffset.UtcNow.Subtract(creationTime.Value);
      }
      return null;
    }

    private async Task WriteEntry(AppendBlobEntry entry) {
      var blob = _container.GetAppendBlobReference(entry.BlobName);
      await blob.UploadTextAsync(entry.Content);
    }

    private async Task WriteRequest(Stream stream, StreamWriter streamWriter, HttpRequestMessage message) {
      streamWriter.WriteLine($"{message.Method} {message.RequestUri.PathAndQuery} HTTP/{message.Version}");

      foreach (var header in message.Headers) {
        streamWriter.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
      }

      if (message.Content != null) {
        foreach (var header in message.Content.Headers) {
          streamWriter.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
      }

      streamWriter.WriteLine();

      if (message.Content != null) {
        streamWriter.Flush();
        await message.Content.LoadIntoBufferAsync();
        await message.Content.CopyToAsync(stream);
        streamWriter.WriteLine();
      }
    }

    private async Task WriteResponse(Stream stream, StreamWriter streamWriter, HttpResponseMessage message) {
      streamWriter.WriteLine($"HTTP/{message.Version} {(int)message.StatusCode} {message.ReasonPhrase}");
      foreach (var header in message.Headers) {
        streamWriter.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
      }
      if (message.Content != null) {
        foreach (var header in message.Content.Headers) {
          streamWriter.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
      }

      streamWriter.WriteLine();

      if (message.Content != null) {
        streamWriter.Flush();
        await message.Content.LoadIntoBufferAsync();
        await message.Content.CopyToAsync(stream);
      }
    }

    protected override void Dispose(bool disposing) {
      base.Dispose(disposing);

      if (disposing) {
        if (_initLock != null) {
          _initLock.Dispose();
          _initLock = null;
        }

        if (_logSubscription != null) {
          _logSubscription.Dispose();
          _logSubscription = null;
        }

        if (_appendBlobs != null) {
          _appendBlobs.Dispose();
          _appendBlobs = null;
        }
      }
    }
  }
}
