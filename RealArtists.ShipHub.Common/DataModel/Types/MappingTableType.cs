namespace RealArtists.ShipHub.Common.DataModel.Types {
  public class MappingTableType {
    public MappingTableType() { }

    public MappingTableType(long item1, long item2) {
      Item1 = item1;
      Item2 = item2;
    }

    public long Item1 { get; set; }
    public long Item2 { get; set; }
  }
}
