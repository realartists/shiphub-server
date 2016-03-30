namespace RealArtists.ShipHub.Api.AGModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.Diagnostics.CodeAnalysis;

  public partial class AccessToken {
    [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    public AccessToken() {
      MetaData = new HashSet<GitHubMetaData>();
    }

    public long Id { get; set; }

    public int AccountId { get; set; }

    [Required]
    [StringLength(64)]
    public string ApplicationId { get; set; }

    [Required]
    [StringLength(64)]
    public string Token { get; set; }

    [Required]
    [StringLength(255)]
    public string Scopes { get; set; }

    public int RateLimit { get; set; }

    public int RateLimitRemaining { get; set; }

    public DateTimeOffset RateLimitReset { get; set; }

    public virtual Account Account { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<GitHubMetaData> MetaData { get; set; }
  }
}
