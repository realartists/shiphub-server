namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("GitHubMetaData")]
    public partial class GitHubMetaData
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public GitHubMetaData()
        {
            Accounts = new HashSet<Account>();
            Issues = new HashSet<Issue>();
            Repositories = new HashSet<Repository>();
            Repositories1 = new HashSet<Repository>();
        }

        public long Id { get; set; }

        [StringLength(64)]
        public string ETag { get; set; }

        public DateTimeOffset? Expires { get; set; }

        public DateTimeOffset? LastModified { get; set; }

        public DateTimeOffset? LastRefresh { get; set; }

        public long? AccessTokenId { get; set; }

        public virtual AccessToken AccessToken { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Account> Accounts { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Issue> Issues { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Repository> Repositories { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Repository> Repositories1 { get; set; }
    }
}
