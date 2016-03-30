namespace RealArtists.ShipHub.Api.AGModel
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class PollQueueItem
    {
        [Key]
        [Column(Order = 0)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }

        [Key]
        [Column(Order = 1)]
        public string ResourceType { get; set; }

        [Key]
        [Column(Order = 2)]
        public string ResourceName { get; set; }

        [Key]
        [Column(Order = 3)]
        public DateTimeOffset NotBefore { get; set; }

        [Key]
        [Column(Order = 4)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Impersonate { get; set; }

        [Key]
        [Column(Order = 5)]
        public bool Spider { get; set; }

        public string Extra { get; set; }
    }
}
