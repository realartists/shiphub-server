namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("Users", Schema = "Ship")]
  public class ShipUserModel {
    public Guid Id { get; set; } = Guid.NewGuid();

    public int GitHubAccountId { get; set; }

    public DateTimeOffset CreationDate { get; set; }

    public virtual GitHubAccountModel GithubAccount { get; set; }

    public virtual ICollection<ShipAuthenticationTokenModel> AuthenticationTokens { get; set; } = new HashSet<ShipAuthenticationTokenModel>();
  }
}
