namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("RepositoryLog")]
    public partial class RepositoryLog
    {
        public long Id { get; set; }

        public long RepositoryId { get; set; }

        [Required]
        [StringLength(20)]
        public string Type { get; set; }

        public long ItemId { get; set; }

        public bool Delete { get; set; }

        public long? RowVersion { get; set; }

        public virtual Repository Repository { get; set; }
    }
}
