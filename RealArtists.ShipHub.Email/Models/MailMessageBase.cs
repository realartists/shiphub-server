namespace RealArtists.ShipHub.Email.Models {
  using System.Linq;

  public abstract class MailMessageBase {
    public string ToAddress { get; set; }
    public string ToName { get; set; }
    public string GitHubUserName { get; set; }

    public string FirstName {
      get {
        return ToName.Split(' ').First();
      }
    }
  }
}