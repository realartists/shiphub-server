namespace RealArtists.ShipHub.Email.Models {
  using System;

  public class PurchasePersonalMailMessage : MailMessageBase {
    public bool BelongsToOrganization { get; set; }
    public bool WasGivenTrialCredit { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
  }
}