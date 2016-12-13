namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentFailedMailMessage : MailMessageBase {
    public double Amount { get; set; }
    public DateTimeOffset? NextRetryDate { get; set; }
    public string ErrorText { get; set; }
    public string InvoicePdfUrl { get; set; }
    public string LastCardDigits { get; set; }
    public string UpdatePaymentMethodUrl { get; set; }
  }
}