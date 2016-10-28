namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PurchaseOrganizationMailMessage : MailMessageBase {
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
  }
}