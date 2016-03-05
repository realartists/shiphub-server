namespace RealArtists.GitHub {
  using System;
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
  }
}
