namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using Newtonsoft.Json;

  public class Comment {
    public long Id { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Account User { get; set; }
    public Reactions Reactions { get; set; }

    // Undocumented. Of course.
    private string _issueUrl;
    public string IssueUrl {
      get { return _issueUrl; }
      set {
        _issueUrl = value;
        IssueNumber = null;

        if (!string.IsNullOrWhiteSpace(_issueUrl)) {
          var parts = _issueUrl.Split('/');
          IssueNumber = int.Parse(parts[parts.Length - 1]);
        }
      }
    }

    [JsonIgnore]
    public int? IssueNumber { get; private set; }
  }
}
