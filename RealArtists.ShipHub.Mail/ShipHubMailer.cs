namespace RealArtists.ShipHub.Mail {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Net;
  using System.Net.Mail;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Hosting;
  using Microsoft.Azure;
  using Models;
  using RazorEngine.Configuration;
  using RazorEngine.Templating;

  public interface IShipHubMailer {
    Task CancellationScheduled(CancellationScheduledMailMessage model);
    Task CardExpiryReminder(CardExpiryReminderMailMessage model);
    Task PaymentFailed(PaymentFailedMailMessage model);
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

    private async Task SendMailMessage(MailMessageBase model, string subject, string templateBaseName, Attachment attachment = null) {
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
        var preheader = razor.RunCompile(templateBaseName + "Plain", model.GetType(), model, new DynamicViewBag(new Dictionary<string, object>() {
          { "SkipHeaderFooter", true }
        })).Trim();
        bag.AddValue("PreHeader", preheader);

        var html = razor.RunCompile(templateBaseName + "Html", model.GetType(), model, bag);

        using (var premailer = new PreMailer.Net.PreMailer(html)) {
          html = premailer.MoveCssInline(
            removeComments: true
            ).Html;
        }

        var htmlView = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html");
        var linkedResource = new LinkedResource(Path.Combine(GetBaseDirectory(), "ShipLogo.png"), "image/png");
        linkedResource.ContentId = "ShipLogo.png";
        htmlView.LinkedResources.Add(linkedResource);
        message.AlternateViews.Add(htmlView);
      }

      if (attachment != null) {
        message.Attachments.Add(attachment);
      }

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

    public Task CancellationScheduled(CancellationScheduledMailMessage model) {
      return SendMailMessage(model, $"Cancellation for {model.GitHubUserName}", "CancellationScheduled");
    }

    public Task CardExpiryReminder(CardExpiryReminderMailMessage model) {
      return SendMailMessage(model, $"Card expiration for {model.GitHubUserName}", "CardExpiryReminder");
    }

    public async Task PaymentFailed(PaymentFailedMailMessage model) {
      using (var stream = new MemoryStream(model.InvoicePdfBytes))
      using (var attachment = new Attachment(stream, $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf")) {
        await SendMailMessage(model, $"Payment failed for {model.GitHubUserName}", "PaymentFailed", attachment);
      }
    }

    public async Task PaymentRefunded(PaymentRefundedMailMessage model) {
      using (var stream = new MemoryStream(model.CreditNotePdfBytes))
      using (var attachment = new Attachment(stream, $"ship-credit-{model.CreditNoteDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf")) {
        await SendMailMessage(model, $"Payment refunded for {model.GitHubUserName}", "PaymentRefunded", attachment);
      }
    }

    public async Task PurchasePersonal(PurchasePersonalMailMessage model) {
      using (var stream = new MemoryStream(model.InvoicePdfBytes))
      using (var attachment = new Attachment(stream, $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf")) {
        await SendMailMessage(model, $"Ship subscription for {model.GitHubUserName}", "PurchasePersonal", attachment);
      }
    }

    public async Task PurchaseOrganization(PurchaseOrganizationMailMessage model) {
      using (var stream = new MemoryStream(model.InvoicePdfBytes))
      using (var attachment = new Attachment(stream, $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf")) {
        await SendMailMessage(model, $"Ship subscription for {model.GitHubUserName}", "PurchaseOrganization", attachment);
      }
    }

    public async Task PaymentSucceededPersonal(PaymentSucceededPersonalMailMessage model) {
      using (var stream = new MemoryStream(model.InvoicePdfBytes))
      using (var attachment = new Attachment(stream, $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf")) {
        await SendMailMessage(model, $"Payment receipt for {model.GitHubUserName}", "PaymentSucceededPersonal", attachment);
      }
    }

    public async Task PaymentSucceededOrganization(PaymentSucceededOrganizationMailMessage model) {
      using (var stream = new MemoryStream(model.InvoicePdfBytes))
      using (var attachment = new Attachment(stream, $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}.pdf",
        "application/pdf")) {
        await SendMailMessage(model, $"Payment receipt for {model.GitHubUserName}", "PaymentSucceededOrganization", attachment);
      }
    }
  }
}
