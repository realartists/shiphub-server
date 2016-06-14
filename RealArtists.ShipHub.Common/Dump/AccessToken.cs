namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class AccessToken
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public AccessToken()
        {
            GitHubMetaDatas = new HashSet<GitHubMetaData>();
        }

        public long Id { get; set; }

        public long AccountId { get; set; }

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

        public DateTimeOffset CreatedAt { get; set; }

        public virtual Account Account { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<GitHubMetaData> GitHubMetaDatas { get; set; }
    }
}
