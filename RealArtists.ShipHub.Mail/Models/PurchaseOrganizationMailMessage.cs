namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PurchaseOrganizationMailMessage : MailMessageBase {
    public string InvoicePdfUrl { get; set; }
  }
}