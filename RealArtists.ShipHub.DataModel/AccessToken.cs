namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class AccessToken {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int AccountId { get; set; }

    [Required]
    [StringLength(64)]
    public string ApplicationId { get; set; }

    [Required]
    [StringLength(64)]
    public string Token { get; set; }

    [Required]
    [StringLength(255)]
    public string Scopes { get; set; }

    public int RateLimit { get; set; }

    public int RateLimitRemaining { get; set; }

    public DateTimeOffset RateLimitReset { get; set; }

    public virtual Account Account { get; set; }
  }
}
