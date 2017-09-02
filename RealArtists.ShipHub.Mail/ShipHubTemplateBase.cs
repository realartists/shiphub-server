namespace RealArtists.ShipHub.Mail {
  using System;
  using System.Web.WebPages;
  using Models;
  using RazorGenerator.Templating;

  public class ShipHubTemplateBase<T> : RazorTemplateBase where T : MailMessageBase {
    public T Model { get; set; }
    public bool SkipHeaderFooter { get; set; }
    public string PreHeader { get; set; }

    private HelperResult PaymentMethodSummary(bool allowHtml, PaymentMethodSummary info) {
      return new HelperResult(writer => {
        if (info.PaymentMethod == PaymentMethod.CreditCard) {
          if (allowHtml) {
            WriteLiteralTo(writer, $"card ending in <strong>{info.LastCardDigits}</strong>");
          } else {
            WriteLiteralTo(writer, $"card ending in \"{info.LastCardDigits}\"");
          }
        } else if (info.PaymentMethod == PaymentMethod.PayPal) {
          WriteLiteralTo(writer, "PayPal account");
        } else {
          throw new NotSupportedException();
        }
      });
    }

    public HelperResult PaymentMethodSummaryPlain(PaymentMethodSummary info) {
      return PaymentMethodSummary(false, info);
    }

    public HelperResult PaymentMethodSummaryHtml(PaymentMethodSummary info) {
      return PaymentMethodSummary(true, info);
    }
  }
}
