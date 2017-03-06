namespace RealArtists.ShipHub.Mail.Models {
  using System.Linq;

  public abstract class MailMessageBase {
    public string ToAddress { get; set; }
    public string ToName { get; set; }
    public string GitHubUserName { get; set; }

    public string GreetingName {
      get {
        if (!string.IsNullOrWhiteSpace(ToName)) {
          return ToName.Split(' ').First();
        } else {
          return GitHubUserName;
        }
      }
    }
  }
}