namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.IO;
  using System.Net;
  using System.Net.Fakes;
  using System.Web;
  using ChargeBee.Api;
  using ChargeBee.Api.Fakes;
  using Microsoft.QualityTools.Testing.Fakes;
  using Newtonsoft.Json.Linq;

  public class ChargeBeeTestUtil {
    /// <summary>
    /// ChargeBee's client library is not easily tested.  Instead of trying to shim all
    /// of its things, we'll use this to watch outgoing HTTP requests and return fake
    /// responses.
    /// </summary>
    public static void ShimChargeBeeWebApi(Func<string, string, Dictionary<string, string>, object> callback) {
      Dictionary<object, MemoryStream> streams = new Dictionary<object, MemoryStream>();

      // Workaround to make the request body of HttpWebRequest inspectable after
      // it has been written to.
      ShimWebRequest.CreateString = (string url) => {
        HttpWebRequest req = (HttpWebRequest)ShimsContext.ExecuteWithoutShims(() => WebRequest.Create(url));
        var shim = new ShimHttpWebRequest(req) {
          // Force a MemoryStream to be returned.  Otherwise, we won't be able
          // to inspect the request body (it's normally write-only).
          GetRequestStream = () => {
            if (!streams.ContainsKey(req)) {
              streams[req] = new MemoryStream();
            }
            return streams[req];
          },
        };
        return shim;
      };

      ApiConfig.Configure("fake-site-name", "fake-site-key");
      ShimApiUtil.SendRequestHttpWebRequestHttpStatusCodeOut =
        (HttpWebRequest req, out HttpStatusCode code) => {
          NameValueCollection nvc;

          if (req.Method.Equals("POST")) {
            var stream = req.GetRequestStream();
            stream.Position = 0;
            string body;
            using (var reader = new StreamReader(stream)) {
              body = reader.ReadToEnd();
            }
            nvc = HttpUtility.ParseQueryString(body);
          } else {
            nvc = HttpUtility.ParseQueryString(req.RequestUri.Query);
          }

          var data = new Dictionary<string, string>();
          foreach (var key in nvc.AllKeys) {
            data[key] = nvc[key];
          }

          code = HttpStatusCode.OK;
          object result = callback(req.Method, req.RequestUri.AbsolutePath, data);
          return JToken.FromObject(result).ToString(Newtonsoft.Json.Formatting.Indented);
        };
    }

  }
}
