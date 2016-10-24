﻿namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class PurchasePersonalMailMessage : MailMessageBase {
    public string FirstName { get; set; }
    public bool BelongsToOrganization { get; set; }
    public bool WasGivenTrialCredit { get; set; }
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
  }
}