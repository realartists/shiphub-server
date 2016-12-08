namespace RealArtists.ShipHub.Email.Models {
  using System;

  public class CancellationScheduledMailMessage : MailMessageBase {
    public DateTimeOffset CurrentTermEnd { get; set; }
  }
}