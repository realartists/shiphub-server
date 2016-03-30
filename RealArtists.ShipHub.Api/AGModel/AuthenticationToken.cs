namespace RealArtists.ShipHub.Api.AGModel
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class AuthenticationToken
    {
        [Key]
        public Guid Token { get; set; }

        public int AccountId { get; set; }

        [Required]
        [StringLength(150)]
        public string ClientName { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        public DateTimeOffset LastAccessDate { get; set; }

        public virtual Account Account { get; set; }
    }
}
