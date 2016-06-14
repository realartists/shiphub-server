namespace RealArtists.ShipHub.Common.Dump
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class Issue
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Issue()
        {
            Comments = new HashSet<Comment>();
            Labels = new HashSet<Label>();
        }

        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }

        public long UserId { get; set; }

        public long RepositoryId { get; set; }

        public int Number { get; set; }

        [Required]
        [StringLength(6)]
        public string State { get; set; }

        [Required]
        public string Title { get; set; }

        public string Body { get; set; }

        public long? AssigneeId { get; set; }

        public long? MilestoneId { get; set; }

        public bool Locked { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset? ClosedAt { get; set; }

        public long? ClosedById { get; set; }

        public string Reactions { get; set; }

        public long? MetaDataId { get; set; }

        public virtual Account Account { get; set; }

        public virtual Account Account1 { get; set; }

        public virtual Account Account2 { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Comment> Comments { get; set; }

        public virtual GitHubMetaData GitHubMetaData { get; set; }

        public virtual Milestone Milestone { get; set; }

        public virtual Repository Repository { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Label> Labels { get; set; }
    }
}
