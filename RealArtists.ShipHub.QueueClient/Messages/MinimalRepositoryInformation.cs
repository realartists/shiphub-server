namespace RealArtists.ShipHub.QueueClient.Messages {
  public class MinimalRepositoryInformation {
    public int Id { get; set; }
    public int AccountId { get; set; }
    public bool Private { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
  }
}
