namespace RealArtists.ShipHub.Api.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net.Http;
  using System.Text.RegularExpressions;

  public class GitHubPagination {
    // Link: <https://api.github.com/repositories/51336290/issues/events?page_size=5&page=2>; rel="next", <https://api.github.com/repositories/51336290/issues/events?page_size=5&page=3>; rel="last"
    const RegexOptions _RegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase;
    static readonly Regex _LinkRegex = new Regex(@"<(?<link>[^>]+)>; rel=""(?<rel>first|last|next|prev){1}""(, )?", _RegexOptions);

    public Uri Next { get; set; }
    public Uri Last { get; set; }
    public Uri First { get; set; }
    public Uri Previous { get; set; }

    public static GitHubPagination FromLinkHeader(string linkHeaderValue) {
      var links = new GitHubPagination();
      foreach (Match match in _LinkRegex.Matches(linkHeaderValue)) {
        var linkUri = new Uri(match.Groups["link"].Value);
        switch (match.Groups["rel"].Value) {
          case "first":
            links.First = linkUri;
            break;
          case "last":
            links.Last = linkUri;
            break;
          case "next":
            links.Next = linkUri;
            break;
          case "prev":
            links.Previous = linkUri;
            break;
          default:  // Skip unknown values
            break;
        }
      }
      return links;
    }

    public bool CanInterpolate {
      get {
        if (Next == null || Last == null) {
          return false;
        }

        int pageNext, pageLast;
        return int.TryParse(Next.ParseQueryString().Get("page"), out pageNext)
          && int.TryParse(Last.ParseQueryString().Get("page"), out pageLast);
      }
    }

    public IEnumerable<Uri> Interpolate() {
      if (Next == null || Last == null) {
        throw new InvalidOperationException("Next and Last are required to interpolate.");
      }

      int pageNext, pageLast;
      var parsedNext = Next.ParseQueryString();
      if (!int.TryParse(parsedNext.Get("page"), out pageNext)
        || !int.TryParse(Last.ParseQueryString().Get("page"), out pageLast)) {
        throw new InvalidOperationException("Only page based interpolation is supported.");
      }

      if (pageLast < pageNext) {
        throw new InvalidOperationException("Impossible state detected.");
      }

      var builder = new UriBuilder(Next);
      for (int i = pageNext; i <= pageLast; ++i) {
        parsedNext.Set("page", i.ToString());
        builder.Query = parsedNext.ToString();
        yield return builder.Uri;
      }
    }
  }
}
