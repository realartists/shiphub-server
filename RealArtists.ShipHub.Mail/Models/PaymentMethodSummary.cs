namespace RealArtists.ShipHub.Mail.Models {
  public enum PaymentMethod {
    Unknown = 0,
    CreditCard,
    PayPal,
  }

  public class PaymentMethodSummary {
    public PaymentMethod PaymentMethod { get; set; }
    public string LastCardDigits { get; set; }
  }
}