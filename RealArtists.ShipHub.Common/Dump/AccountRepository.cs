namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class AccountRepository
    {
        [Key]
        [Column(Order = 0)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long AccountId { get; set; }

        [Key]
        [Column(Order = 1)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long RepositoryId { get; set; }

        public bool Hidden { get; set; }

        public virtual Account Account { get; set; }

        public virtual Repository Repository { get; set; }
    }
}
