namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PaymentRefundedMailMessage : MailMessageBase {
    public double AmountRefunded { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public byte[] CreditNotePdfBytes { get; set; }
    public DateTimeOffset CreditNoteDate { get; set; }
    public string LastCardDigits { get; set; }
  }
}