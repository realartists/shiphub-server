namespace RealArtists.ShipHub.Mail.Models {
  public abstract class MailMessageBase {
    public string ToAddress { get; set; }
    public string ToName { get; set; }
  }
}