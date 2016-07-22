namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using GitHub;

  [Table("GitHubMetaData")]
  public class GitHubMetaData : IGitHubRequestOptions, IGitHubCacheOptions {
    public long Id { get; set; }

    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? Expires { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public DateTimeOffset? LastRefresh { get; set; }

    public long? AccountId { get; set; }

    public virtual User Account { get; set; }

    // Implementing IGitHubRequestOptions lets us use this to easily cache GitHub responses

    public IGitHubCredentials Credentials {
      get {
        if (AccountId != null && !string.IsNullOrWhiteSpace(Account.Token)) {
          return GitHubCredentials.ForToken(Account.Token);
        }
        return null;
      }
    }

    public IGitHubCacheOptions CacheOptions { get { return this; } }
  }
}
