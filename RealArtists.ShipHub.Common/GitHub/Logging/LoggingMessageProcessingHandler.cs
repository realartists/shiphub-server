namespace RealArtists.ShipHub.Common.GitHub.Logging {
  using System;
  using System.Diagnostics;
  using System.Linq;
  using System.Net.Http;
  using System.Reactive;
  using System.Reactive.Concurrency;
  using System.Reactive.Linq;
  using System.Reactive.Subjects;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Microsoft.WindowsAzure.Storage;
  using Microsoft.WindowsAzure.Storage.Blob;

  public class AppendBlobEntry {
    public bool Create { get; set; }
    public string BlobName { get; set; }
    public string Content { get; set; }
  }

  public class LoggingMessageProcessingHandler : MessageProcessingHandler {
    public const string LogBlobNameKey = "LogBlobName";

    private CloudBlobClient _blobClient;
    private string _containerName;

    private Subject<AppendBlobEntry> _appendBlobs;
    private CloudBlobContainer _container;
    private IDisposable _logSubscription;

    public LoggingMessageProcessingHandler(
      CloudStorageAccount storageAccount,
      string containerName,
      HttpMessageHandler innerHandler) : base(innerHandler) {
      _blobClient = storageAccount.CreateCloudBlobClient();
      _containerName = containerName;
    }

    private bool _initialized = false;
    public async Task Initialize() {
      if (_initialized) {
        return;
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
    }

    private IObservable<T> LogError<T>(Exception exception) {
#if DEBUG
      Debugger.Break();
#endif
      return Observable.Empty<T>();
    }

    protected override HttpRequestMessage ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken) {
      var blobName = ExtractBlobName(request);
      if (!blobName.IsNullOrWhiteSpace()) {
        var blob = new AppendBlobEntry() {
          Create = true,
          BlobName = blobName,
          Content = Stringify(request).GetAwaiter().GetResult(),
        };
        _appendBlobs.OnNext(blob);
      }

      return request;
    }

    protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken) {
      var blobName = ExtractBlobName(response.RequestMessage);
      if (!blobName.IsNullOrWhiteSpace()) {
        var blob = new AppendBlobEntry() {
          Create = false,
          BlobName = blobName,
          Content = Stringify(response).GetAwaiter().GetResult(),
        };
        _appendBlobs.OnNext(blob);
      }

      return response;
    }

    private string ExtractBlobName(HttpRequestMessage request) {
      string blobName = null;
      if (request.Properties.ContainsKey(LogBlobNameKey)) {
        blobName = request.Properties[LogBlobNameKey] as string;
      }
      return blobName;
    }

    private async Task WriteEntry(AppendBlobEntry entry) {
      var blob = _container.GetAppendBlobReference(entry.BlobName);
      if (entry.Create) {
        await blob.CreateOrReplaceAsync();
      }
      await blob.AppendTextAsync(entry.Content);
    }

    // TODO: Streams?
    private async Task<string> Stringify(HttpRequestMessage message) {
      var s = new StringBuilder();
      s.AppendLine($"{message.Method} {message.RequestUri.PathAndQuery} HTTP/{message.Version}");
      foreach (var header in message.Headers) {
        s.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
      }
      if (message.Content != null) {
        foreach (var header in message.Content.Headers) {
          s.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
      }

      s.AppendLine();

      if (message.Content != null) {
        s.Append(await message.Content.ReadAsStringAsync());
      }

      return s.ToString();
    }

    private async Task<string> Stringify(HttpResponseMessage message) {
      var s = new StringBuilder();
      s.AppendLine($"HTTP/{message.Version} {(int)message.StatusCode} {message.ReasonPhrase}");
      foreach (var header in message.Headers) {
        s.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
      }
      if (message.Content != null) {
        foreach (var header in message.Content.Headers) {
          s.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
      }

      s.AppendLine();

      if (message.Content != null) {
        s.Append(await message.Content.ReadAsStringAsync());
      }

      return s.ToString();
    }

    protected override void Dispose(bool disposing) {
      base.Dispose(disposing);

      if (disposing) {
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
