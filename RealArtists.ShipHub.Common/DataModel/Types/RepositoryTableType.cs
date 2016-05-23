namespace RealArtists.ShipHub.Common.DataModel.Types {
  public class RepositoryTableType {
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public bool Private { get; set; }
  }
}
