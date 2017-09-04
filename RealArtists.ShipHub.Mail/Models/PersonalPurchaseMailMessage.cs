namespace RealArtists.ShipHub.Mail.Models {
  public class PurchasePersonalMailMessage : MailMessageBase, IPdfAttachment {
    public bool BelongsToOrganization { get; set; }
    public bool WasGivenTrialCredit { get; set; }
    public string InvoicePdfUrl { get; set; }
    public string AttachmentUrl { get; set; }
    public string AttachmentName { get; set; }
  }
}
