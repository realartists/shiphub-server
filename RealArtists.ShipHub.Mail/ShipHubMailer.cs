namespace RealArtists.ShipHub.Mail {
  using System;
  using System.IO;
  using System.Net;
  using System.Net.Mail;
  using System.Net.Mime;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Hosting;
  using Microsoft.Azure;
  using RazorEngine.Configuration;
  using RazorEngine.Templating;
  using Models;

  public interface IShipHubMailer {
    Task PaymentRefunded(PaymentRefundedMailMessage model);
    Task PaymentSucceededPersonal(PaymentSucceededPersonalMailMessage model);
    Task PaymentSucceededOrganization(PaymentSucceededOrganizationMailMessage model);
    Task PurchasePersonal(PurchasePersonalMailMessage model);
    Task PurchaseOrganization(PurchaseOrganizationMailMessage model);
  }

  public class ShipHubMailer : IShipHubMailer {
    public bool IncludeHtmlView { get; set; } = true;

    private string GetBaseDirectory() {
      if (HostingEnvironment.IsHosted) {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
      } else {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
      }
    }

    private IRazorEngineService GetRazorEngineService() {
      var config = new TemplateServiceConfiguration {
        CachingProvider = new DefaultCachingProvider(_ => { }),
        DisableTempFileLocking = true,
        TemplateManager = new ResolvePathTemplateManager(new[] {
          Path.Combine(GetBaseDirectory(), "Views"),
        })
      };
      return RazorEngineService.Create(config);
    }

    private MailMessage CreateMailMessage(MailMessageBase model, string subject, string templateBaseName) {
      var razor = GetRazorEngineService();
      var text = razor.RunCompile(templateBaseName + "Plain", model.GetType(), model);

      var message = new MailMessage(
        new MailAddress("support@realartists.com", "Ship"),
        new MailAddress(model.ToAddress, model.ToName));
      message.Subject = subject;
      message.Body = text;

      if (IncludeHtmlView) {
        var bag = new DynamicViewBag();
        // Let's just use the entire plain text version as the pre-header for now.
        // We don't need to do anything more clever.  Also, it's important that
        // pre-header text be sufficiently long so that the <img> tag's alt text and
        // the href URL don't leak into the pre-header.  The plain text version is long
        // enough for this.
        bag.AddValue("PreHeader", text);
        var html = razor.RunCompile(templateBaseName + "Html", model.GetType(), model, bag);

        var premailer = new PreMailer.Net.PreMailer(html);
        var htmlProcessed = premailer.MoveCssInline(
          removeComments: true
          ).Html;

        var htmlView = new AlternateView(
          new MemoryStream(UTF8Encoding.Default.GetBytes(htmlProcessed)),
          new ContentType("text/html"));

        var linkedResource = new LinkedResource(Path.Combine(GetBaseDirectory(), "ShipLogo.png"), "image/png");
        linkedResource.ContentId = "ShipLogo.png";
        htmlView.LinkedResources.Add(linkedResource);
        message.AlternateViews.Add(htmlView);
      }

      return message;
    }

    private async Task SendMessage(MailMessage message) {
      var password = CloudConfigurationManager.GetSetting("SmtpPassword");

      if (password != null) {
        using (var client = new SmtpClient()) {

          client.Host = "smtp.mailgun.org";
          client.Port = 587;
          client.Credentials = new NetworkCredential(
            "shiphub@email.realartists.com",
            password);
          await client.SendMailAsync(message);
        }
      } else {
        Console.WriteLine("SmtpPassword unset so will not send email.");
      }
    }

    public Task PaymentRefunded(PaymentRefundedMailMessage model) {
      var message = CreateMailMessage(model, $"Payment refunded for {model.GitHubUsername}", "PaymentRefunded");

      message.Attachments.Add(new Attachment(
        new MemoryStream(model.CreditNotePdfBytes),
        $"ship-credit-{model.CreditNoteDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf"));

      return SendMessage(message);
    }

    public Task PurchasePersonal(PurchasePersonalMailMessage model) {
      var message = CreateMailMessage(model, $"Ship subscription for {model.GitHubUsername}", "PurchasePersonal");

      message.Attachments.Add(new Attachment(
        new MemoryStream(model.InvoicePdfBytes),
        $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf"));

      return SendMessage(message);
    }

    public Task PurchaseOrganization(PurchaseOrganizationMailMessage model) {
      var message = CreateMailMessage(model, $"Ship subscription for {model.GitHubUsername}", "PurchaseOrganization");

      message.Attachments.Add(new Attachment(
        new MemoryStream(model.InvoicePdfBytes),
        $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf"));

      return SendMessage(message);
    }

    public Task PaymentSucceededPersonal(PaymentSucceededPersonalMailMessage model) {
      var message = CreateMailMessage(model, $"Payment receipt for {model.GitHubUsername}", "PaymentSucceededPersonal");

      message.Attachments.Add(new Attachment(
        new MemoryStream(model.InvoicePdfBytes),
        $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf"));

      return SendMessage(message);
    }

    public Task PaymentSucceededOrganization(PaymentSucceededOrganizationMailMessage model) {
      var message = CreateMailMessage(model, $"Payment receipt for {model.GitHubUsername}", "PaymentSucceededOrganization");

      message.Attachments.Add(new Attachment(
        new MemoryStream(model.InvoicePdfBytes),
        $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf"));

      return SendMessage(message);
    }
  }
}
