namespace RealArtists.ShipHub.Common.DataModel.Types {
  public class IssueMappingTableType {
    public IssueMappingTableType() { }

    public IssueMappingTableType(long mappedId, int issueNumber)
      : this(mappedId, issueNumber, null) { }

    public IssueMappingTableType(long mappedId, int issueNumber, long? issueId) {
      MappedId = mappedId;
      IssueNumber = issueNumber;
      IssueId = issueId;
    }

    public int IssueNumber { get; set; }
    public long? IssueId { get; set; }
    public long MappedId { get; set; }
  }
}
