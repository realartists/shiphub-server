namespace RealArtists.ShipHub.Api.Models {
  using System.Collections.Generic;

  public class ApiUser : ApiAccount {
    public IEnumerable<ApiOrganization> Organizations { get; set; }
  }
}