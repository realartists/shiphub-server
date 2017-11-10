namespace RealArtists.ShipHub.Mail.Models {
  public class PaymentRefundedMailMessage : MailMessageBase, IPdfAttachment {
    public double AmountRefunded { get; set; }
    public string CreditNotePdfUrl { get; set; }
    public PaymentMethodSummary PaymentMethodSummary { get; set; }
    public string AttachmentUrl { get; set; }
    public string AttachmentName { get; set; }
  }
}
