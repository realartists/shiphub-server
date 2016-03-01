namespace RealArtists.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net.Http;
  using System.Threading.Tasks;

  public class GitHubRequest {
    public HttpMethod Method { get; set; }
    public string Path { get; set; }
    public string ETag { get; set; }
    public int MyProperty { get; set; }
  }
}
