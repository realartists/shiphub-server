namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PurchasePersonalMailMessage : MailMessageBase {
    public bool BelongsToOrganization { get; set; }
    public bool WasGivenTrialCredit { get; set; }
    public string InvoicePdfUrl { get; set; }
  }
}