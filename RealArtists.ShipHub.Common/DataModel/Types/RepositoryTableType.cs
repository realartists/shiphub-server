namespace RealArtists.ShipHub.Common.DataModel.Types {
  public class RepositoryTableType {
    public int Id { get; set; }
    public int AccountId { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
  }
}
