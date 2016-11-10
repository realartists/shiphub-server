namespace RealArtists.ShipHub.Common.GitHub.Logging {
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
  using Microsoft.WindowsAzure.Storage;
  using Microsoft.WindowsAzure.Storage.Blob;

  public class AppendBlobEntry {
    public string BlobName { get; set; }
    public string Content { get; set; }
  }

  public class LoggingMessageProcessingHandler : DelegatingHandler {
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
      if (storageAccount != null) {
        _blobClient = storageAccount.CreateCloudBlobClient();
        _containerName = containerName;
      }
    }

    private bool _initialized = false;
    public async Task Initialize() {
      if (_initialized) {
        return;
      }
      _initialized = true;

      if (_blobClient == null) {
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

    public static void SetLogBlobName(HttpRequestMessage request, string blobName) {
      request.Properties[LogBlobNameKey] = blobName;
    }

    private IObservable<T> LogError<T>(Exception exception) {
#if DEBUG
      Debugger.Break();
#endif
      return Observable.Empty<T>();
    }

    protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      var blobName = ExtractBlobName(request);

      if (_blobClient == null || blobName.IsNullOrWhiteSpace()) {
        // log that the request happened, but don't store to blob
        var response = await base.SendAsync(request, cancellationToken);
        Log.Info($"{request.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version} - {(int)response.StatusCode} {response.ReasonPhrase}");
        return response;
      }

      if (!_initialized) {
        await Initialize();
      }

      using (var ms = new MemoryStream())
      using (var sw = new StreamWriter(ms, Encoding.UTF8)) {
        await WriteRequest(ms, sw, request);
        var response = await base.SendAsync(request, cancellationToken);
        await WriteResponse(ms, sw, response);

        _appendBlobs.OnNext(new AppendBlobEntry() {
          BlobName = blobName,
          Content = Encoding.UTF8.GetString(ms.ToArray()),
        });

        long contentLength = response?.Content?.Headers?.ContentLength ?? 0;
        Log.Info($"{request.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version} - {(int)response.StatusCode} {response.ReasonPhrase} - {blobName} - {contentLength} bytes");

        return response;
      }
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
