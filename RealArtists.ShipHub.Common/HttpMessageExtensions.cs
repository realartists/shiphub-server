namespace RealArtists.ShipHub.Common {
  using System;
  using System.Linq;
  using System.Net.Http;
  using System.Net.Http.Headers;

  public static class HttpMessageExtensions {
    public static T ParseHeader<T>(this HttpHeaders headers, string headerName, Func<string, T> selector) {
      var header = headers
        .Where(x => x.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .SingleOrDefault();

      return selector(header);
    }

    public static T ParseHeader<T>(this HttpResponseMessage response, string headerName, Func<string, T> selector) {
      return response.Headers.ParseHeader(headerName, selector);
    }

    public static T ParseHeader<T>(this HttpRequestMessage request, string headerName, Func<string, T> selector) {
      return request.Headers.ParseHeader(headerName, selector);
    }
  }
}
