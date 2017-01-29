namespace RealArtists.ShipHub.Common {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;

  public static class HttpUtilities {
    public static void SetServicePointDefaultConnectionLimit(int maxConnections = int.MaxValue) {
      ServicePointManager.DefaultConnectionLimit = maxConnections;
    }

    public static void SetServicePointConnectionLimit(Uri uri, int maxConnections = int.MaxValue) {
      var servicePoint = ServicePointManager.FindServicePoint(uri);
      servicePoint.ConnectionLimit = maxConnections;
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "It returns it.")]
    public static HttpClientHandler CreateDefaultHandler(bool useFiddler = false, int maxRedirects = 0) {
      var handler = new HttpClientHandler() {
        AllowAutoRedirect = maxRedirects > 0,
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        CheckCertificateRevocationList = true,
        MaxConnectionsPerServer = int.MaxValue,
        MaxAutomaticRedirections = Math.Max(maxRedirects, 1),
        UseCookies = false,
        UseDefaultCredentials = false,
        UseProxy = false,
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
      };

#if DEBUG
      if (useFiddler) {
        handler.UseProxy = true;
        handler.Proxy = new WebProxy("127.0.0.1", 8888);
        handler.ServerCertificateCustomValidationCallback = (request, cert, chain, sslPolicyErrors) => { return true; };
      }
#endif

      return handler;
    }
  }
}
