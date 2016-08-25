namespace RealArtists.ShipHub.Common {
  using System;
  using System.Linq;
  using System.Net.Http;

  public static class HttpMessageExtensions {
    public static T ParseHeader<T>(this HttpResponseMessage response, string headerName, Func<string, T> selector) {
      var header = response.Headers
        .Where(x => x.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .SingleOrDefault();

      return selector(header);
    }
  }
}
