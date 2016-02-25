namespace RealArtists.ShipHub.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("AuthenticationTokens", Schema = "GitHub")]
  public class GitHubAuthenticationTokenModel {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int AccountId { get; set; }

    [Required]
    [StringLength(512)]
    public string AccessToken { get; set; }

    [Required]
    public string Scopes { get; set; }

    public virtual GitHubAccountModel Account { get; set; }
  }
}
