namespace RealArtists.GitHub.Models {
  public class Repository : GitHubModel {
    public int Id { get; set; }
    public Account Owner { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public string Description { get; set; }
    public bool Private { get; set; }
    public bool Fork { get; set; }
    public bool HasIssues { get; set; }
    public Account Organization { get; set; }
  }
}
