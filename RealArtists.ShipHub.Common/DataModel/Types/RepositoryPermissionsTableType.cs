namespace RealArtists.ShipHub.Common.DataModel.Types {
  public class RepositoryPermissionsTableType {
    public long RepositoryId { get; set; }
    public bool Admin { get; set; }
    public bool Push { get; set; }
    public bool Pull { get; set; }
  }
}
