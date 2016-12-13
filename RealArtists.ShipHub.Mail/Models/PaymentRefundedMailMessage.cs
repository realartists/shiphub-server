namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentRefundedMailMessage : MailMessageBase {
    public double AmountRefunded { get; set; }
    public string CreditNotePdfUrl { get; set; }
    public string LastCardDigits { get; set; }
  }
}