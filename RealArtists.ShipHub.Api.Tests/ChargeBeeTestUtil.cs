namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Threading;
  using System.Threading.Tasks;
  using ChargeBee;

  public class ChargeBeeTestUtil {
    public const string TestApiRoot = "http://localhost/api/v2/";

    private class InterceptingHandler : HttpMessageHandler {
      private Func<HttpRequestMessage, Task<HttpResponseMessage>> _interceptor;

      public InterceptingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> interceptor) {
        _interceptor = interceptor;
      }

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        return _interceptor(request);
      }
    }

    /// <summary>
    /// ChargeBee's client library is not easily tested.  Instead of trying to shim all
    /// of its things, we'll use this to watch outgoing HTTP requests and return fake
    /// responses.
    /// </summary>
    public static ChargeBeeApi ShimChargeBeeApi(Func<string, string, Dictionary<string, string>, object> callback) {
      var handler = new InterceptingHandler((request) => {
        NameValueCollection nvc;

        if (request.Method == HttpMethod.Post) {
          nvc = request.Content.ReadAsFormDataAsync().GetAwaiter().GetResult();
        } else {
          nvc = request.RequestUri.ParseQueryString();
        }

        var data = new Dictionary<string, string>();
        foreach (var key in nvc.AllKeys) {
          data[key] = nvc[key];
        }

        var result = callback(request.Method.Method, request.RequestUri.AbsolutePath, data);
        var formatter = new JsonMediaTypeFormatter();
        formatter.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
        var response = request.CreateResponse(HttpStatusCode.OK, result, formatter);
        return Task.FromResult(response);
      });

      return new ChargeBeeApi(new Uri(TestApiRoot), null, new HttpClient(handler));
    }

    public static ChargeBeeApi ShimChargeBeeApiPdf() {
      var handler = new InterceptingHandler((request) => {
        var path = request.RequestUri.AbsolutePath;
        var isValidPrefix = path.StartsWith("/api/v2/invoices/") || path.StartsWith("/api/v2/credit_notes/");
        var isValidPostfix = path.EndsWith("/pdf");
        if (!isValidPrefix || !isValidPostfix || request.Method != HttpMethod.Post) {
          return Task.FromResult(request.CreateResponse(HttpStatusCode.NotFound));
        }

        var result = new {
          download = new {
            download_url = "unit-test://invoice",
            valid_till = 1505917851,
            @object = "download",
          },
        };
        var formatter = new JsonMediaTypeFormatter();
        formatter.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
        var response = request.CreateResponse(HttpStatusCode.OK, result, formatter);
        return Task.FromResult(response);
      });

      return new ChargeBeeApi(new Uri(TestApiRoot), null, new HttpClient(handler));
    }
  }
}
