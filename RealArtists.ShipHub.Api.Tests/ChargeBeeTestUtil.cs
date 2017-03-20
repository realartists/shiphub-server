namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.IO;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web;
  using ChargeBee;
  using ChargeBee.Api;
  using Newtonsoft.Json.Linq;

  public class ChargeBeeTestUtil {
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

      return new ChargeBeeApi(new Uri("http://localhost/api/v2/"), null, new HttpClient(handler));
    }
  }
}
