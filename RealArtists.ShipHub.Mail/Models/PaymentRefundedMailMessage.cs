namespace RealArtists.ShipHub.Mail.Models {
  public class PaymentRefundedMailMessage : MailMessageBase {
    public double AmountRefunded { get; set; }
    public string CreditNotePdfUrl { get; set; }
    public PaymentMethodSummary PaymentMethodSummary { get; set; }
  }
}