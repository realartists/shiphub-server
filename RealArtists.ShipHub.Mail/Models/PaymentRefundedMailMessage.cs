namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentRefundedMailMessage : MailMessageBase {
    public double AmountRefunded { get; set; }
    public byte[] CreditNotePdfBytes { get; set; }
    public DateTimeOffset CreditNoteDate { get; set; }
    public string LastCardDigits { get; set; }
  }
}