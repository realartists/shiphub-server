namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class CancellationScheduledMailMessage : MailMessageBase {
    public DateTimeOffset CurrentTermEnd { get; set; }
  }
}