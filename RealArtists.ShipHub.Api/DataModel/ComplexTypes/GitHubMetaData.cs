namespace RealArtists.ShipHub.Api.DataModel.ComplexTypes {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Linq;
  using System.Web;
  using ShipHub.DataModel;

  [ComplexType]
  public class GitHubMetaData {
    // Cache Data
    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? Expires { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public DateTimeOffset LastRefresh { get; set; }

    public long CacheTokenId { get; set; }

    public virtual AccessToken CacheToken { get; set; }
  }
}