namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class Comment
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }

        public long IssueId { get; set; }

        public long RepositoryId { get; set; }

        public long UserId { get; set; }

        [Required]
        public string Body { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public string Reactions { get; set; }

        public virtual Account Account { get; set; }

        public virtual Issue Issue { get; set; }

        public virtual Repository Repository { get; set; }
    }
}
