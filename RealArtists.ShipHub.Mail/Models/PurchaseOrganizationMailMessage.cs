namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PurchaseOrganizationMailMessage : MailMessageBase {
    public string FirstName { get; set; }
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
  }
}