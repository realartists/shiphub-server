namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentSucceededPersonalMailMessage : MailMessageBase, IPdfAttachment {
    public double AmountPaid { get; set; }
    public string InvoicePdfUrl { get; set; }
    public PaymentMethodSummary PaymentMethodSummary { get; set; }
    public DateTimeOffset ServiceThroughDate { get; set; }
    public string AttachmentUrl { get; set; }
    public string AttachmentName { get; set; }
  }
}
