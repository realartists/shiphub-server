namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentFailedMailMessage : MailMessageBase {
    public double Amount { get; set; }
    public DateTimeOffset? NextRetryDate { get; set; }
    public string ErrorText { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
    public string LastCardDigits { get; set; }
    public string UpdatePaymentMethodUrl { get; set; }
  }
}