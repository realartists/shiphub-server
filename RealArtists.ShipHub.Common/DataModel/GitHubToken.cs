namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;

  public class GitHubToken {
    [Key]
    [Required]
    [StringLength(64)]
    public string Token { get; set; }

    public long UserId { get; set; }

    public virtual User User { get; set; }
  }
}
