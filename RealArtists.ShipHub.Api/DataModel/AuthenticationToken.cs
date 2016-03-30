namespace RealArtists.ShipHub.Api.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;

  public class AuthenticationToken {
    [Key]
    public Guid Token { get; set; } = Guid.Empty;

    public int AccountId { get; set; }

    [Required]
    [StringLength(150)]
    public string ClientName { get; set; }

    public DateTimeOffset CreationDate { get; set; }

    public DateTimeOffset LastAccessDate { get; set; }

    public virtual User Account { get; set; }
  }
}
