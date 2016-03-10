namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("AuthenticationTokens", Schema = "Ship")]
  public class AuthenticationToken {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Token { get; set; }

    public Guid AccountId { get; set; }

    [Required]
    [StringLength(150)]
    public string ClientName { get; set; }

    public DateTimeOffset CreationDate { get; set; }

    public DateTimeOffset LastAccessDate { get; set; }

    public virtual Account Account { get; set; }
  }
}
