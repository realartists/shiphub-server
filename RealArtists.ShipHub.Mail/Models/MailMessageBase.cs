namespace RealArtists.ShipHub.Mail.Models {
  using System.Linq;

  public interface IPdfAttachment {
    string AttachmentUrl { get; }
    string AttachmentName { get; }
  }

  public abstract class MailMessageBase {
    public string ToAddress { get; set; }
    public string CustomerName { get; set; }
    public string GitHubUserName { get; set; }
    public string GreetingName => ToName.Split(' ').First();

    public string ToName {
      get {
        if (!string.IsNullOrWhiteSpace(CustomerName)) {
          return CustomerName;
        } else {
          return GitHubUserName;
        }
      }
    }
  }
}
