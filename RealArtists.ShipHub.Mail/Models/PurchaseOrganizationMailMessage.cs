namespace RealArtists.ShipHub.Mail.Models {
  public class PurchaseOrganizationMailMessage : MailMessageBase, IPdfAttachment {
    public string InvoicePdfUrl { get; set; }
    public string AttachmentUrl { get; set; }
    public string AttachmentName { get; set; }
  }
}
