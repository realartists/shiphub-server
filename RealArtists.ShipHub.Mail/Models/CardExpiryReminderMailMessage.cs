namespace RealArtists.ShipHub.Mail.Models {
  using System;

  public class CardExpiryRemdinderMailMessage : MailMessageBase {
    public bool AlreadyExpired { get; set; }
    public long ExpiryMonth { get; set; }
    public long ExpiryYear { get; set; }
    public string LastCardDigits { get; set; }
    public string UpdatePaymentMethodUrl { get; set; }
  }
}