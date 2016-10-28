namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentSucceededPersonalMailMessage : MailMessageBase {
    public double AmountPaid { get; set; }
    public string LastCardDigits { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset ServiceThroughDate { get; set; }
  }
}