namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("AuthenticationTokens", Schema = "Ship")]
  public class ShipAuthenticationTokenModel {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [Required]
    [StringLength(150)]
    public string ClientName { get; set; }

    public DateTimeOffset CreationDate { get; set; }

    public DateTimeOffset LastAccessDate { get; set; }

    public virtual ShipUserModel User { get; set; }
  }
}
