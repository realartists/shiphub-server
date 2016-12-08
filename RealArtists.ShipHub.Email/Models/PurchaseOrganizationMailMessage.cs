namespace RealArtists.ShipHub.Email.Models {
  using System;

  public class PurchaseOrganizationMailMessage : MailMessageBase {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
  }
}