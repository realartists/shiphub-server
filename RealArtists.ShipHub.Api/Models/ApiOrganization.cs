namespace RealArtists.ShipHub.Api.Models {
  using System.Collections.Generic;

  public class ApiOrganization : ApiAccount {
    public IEnumerable<ApiUser> Members { get; set; }
  }
}