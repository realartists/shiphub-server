namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentFailedMailMessage : MailMessageBase, IPdfAttachment {
    public double Amount { get; set; }
    public DateTimeOffset? NextRetryDate { get; set; }
    public string ErrorText { get; set; }
    public string InvoicePdfUrl { get; set; }
    public PaymentMethodSummary PaymentMethodSummary { get; set; }
    public string UpdatePaymentMethodUrl { get; set; }
    public string AttachmentUrl { get; set; }
    public string AttachmentName { get; set; }
  }
}
