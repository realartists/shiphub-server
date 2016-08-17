namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class OrganizationMembership {
    public string State { get; set; }
    public string Role { get; set; }   
    public Account Organization { get; set; }
    public Account User { get; set; }
  }
}
