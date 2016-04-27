namespace RealArtists.Ship.Server.QueueClient.GitHubSpider {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;

  public static class SpiderQueueNames {
    public const string _Prefix = "gh-s-";

    public const string AccessToken = _Prefix + "access-token";
    public const string User = _Prefix + "user";
    public const string Organization = _Prefix + "organization";

  }
}
