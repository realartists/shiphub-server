namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class IssueEvent
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }

        public long RepositoryId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        [Required]
        public string ExtensionData { get; set; }

        public virtual Repository Repository { get; set; }
    }
}
