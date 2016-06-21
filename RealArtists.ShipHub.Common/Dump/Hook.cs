namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class Hook
    {
        [Key]
        [Column(Order = 0)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }

        [Key]
        [Column(Order = 1)]
        public Guid Key { get; set; }

        [Key]
        [Column(Order = 2)]
        public bool Active { get; set; }

        [Key]
        [Column(Order = 3)]
        [StringLength(500)]
        public string Events { get; set; }

        public DateTimeOffset? LastSeen { get; set; }

        [StringLength(500)]
        public string Url { get; set; }

        [Key]
        [Column(Order = 4)]
        [StringLength(500)]
        public string ConfigUrl { get; set; }
    }
}
