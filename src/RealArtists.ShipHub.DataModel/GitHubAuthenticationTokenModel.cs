namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("AuthenticationTokens", Schema = "GitHub")]
  public class GitHubAuthenticationTokenModel {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int AccountId { get; set; }

    [Required]
    [StringLength(64)]
    public string AccessToken { get; set; }

    [Required]
    [StringLength(255)]
    public string Scopes { get; set; }

    public int RateLimit { get; set; }

    public int RateLimitRemaining { get; set; }

    public DateTimeOffset RateLimitReset { get; set; }

    public virtual GitHubAccountModel Account { get; set; }
  }
}
