namespace RealArtists.ShipHub.Common.DataModel.Types {
  public class LabelTableType {
    public long Id { get; set; }
    public string Color { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// IssueId maps labels to issues and is only provided when using
    /// BulkUpdateIssues.
    /// </summary>
    public long? IssueId { get; set; }
  }
}
