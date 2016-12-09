namespace RealArtists.ShipHub.Mail {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.IO.Compression;
  using System.Net;
  using System.Net.Mail;
  using System.Text;
  using System.Threading.Tasks;
  using Common;
  using Mail;
  using Models;
  using RazorGenerator.Templating;
  using Views;

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

    private static Lazy<string> _BaseDirectory = new Lazy<string>(() => {
      var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
      var binDir = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
      if (binDir.Exists) {
        return binDir.FullName;
      } else {
        return dir.FullName;
      }
    });
    private static string BaseDirectory { get { return _BaseDirectory.Value; } }

    private async Task SendMailMessage<T>(
      ShipHubTemplateBase<T> htmlTemplate,
      ShipHubTemplateBase<T> plainTemplate,
      string subject,
      Attachment attachment = null) where T : MailMessageBase {

      var message = new MailMessage(
        new MailAddress("support@realartists.com", "Ship"),
        new MailAddress(htmlTemplate.Model.ToAddress, htmlTemplate.Model.ToName));
      message.Subject = subject;
      message.Body = plainTemplate.TransformText();

      if (IncludeHtmlView) {
        // Let's just use the entire plain text version as the pre-header for now.
        // We don't need to do anything more clever.  Also, it's important that
        // pre-header text be sufficiently long so that the <img> tag's alt text and
        // the href URL don't leak into the pre-header.  The plain text version is long
        // enough for this.
        plainTemplate.Clear();
        plainTemplate.SkipHeaderFooter = true;
        var preheader = plainTemplate.TransformText().Trim();

        htmlTemplate.PreHeader = preheader;
        var html = htmlTemplate.TransformText();

        using (var premailer = new PreMailer.Net.PreMailer(html)) {
          html = premailer.MoveCssInline(
            removeComments: true
            ).Html;
        }

        var htmlView = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html");
        var linkedResource = new LinkedResource(Path.Combine(BaseDirectory, "ShipLogo.png"), "image/png");
        linkedResource.ContentId = "ShipLogo.png";
        htmlView.LinkedResources.Add(linkedResource);
        message.AlternateViews.Add(htmlView);
      }

      if (attachment != null) {
        message.Attachments.Add(attachment);
      }

      var smtpPassword = ShipHubCloudConfiguration.Instance.SmtpPassword;
      if (string.IsNullOrWhiteSpace(smtpPassword)) {
        Console.WriteLine("SmtpPassword unset so will not send email.");
      } else {
        using (var client = new SmtpClient()) {
          client.Host = "smtp.mailgun.org";
          client.Port = 587;
          client.Credentials = new NetworkCredential(
            "shiphub@email.realartists.com",
            smtpPassword);
          await client.SendMailAsync(message);
        }
      }
    }

    public Task CancellationScheduled(CancellationScheduledMailMessage model) {
      return SendMailMessage(
        new CancellationScheduledHtml() { Model = model },
        new CancellationScheduledPlain() { Model = model },
        $"Cancellation for {model.GitHubUserName}");
    }

    public Task CardExpiryReminder(CardExpiryReminderMailMessage model) {
      return SendMailMessage(
        new CardExpiryReminderHtml() { Model = model },
        new CardExpiryReminderPlain() { Model = model },
        $"Card expiration for {model.GitHubUserName}");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "It's not.")]
    private byte[] ZipBytesForPdf(byte[] pdfBytes, string entryName) {
      using (var outStream = new MemoryStream()) {
        using (var archive = new ZipArchive(outStream, ZipArchiveMode.Create, true)) {
          var entry = archive.CreateEntry(entryName);

          using (var entryStream = entry.Open()) {
            entryStream.Write(pdfBytes, 0, pdfBytes.Length);
          }
        }
        return outStream.ToArray();
      }
    }

    public async Task PaymentFailed(PaymentFailedMailMessage model) {
      var baseName = $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}";
      var zipBytes = ZipBytesForPdf(model.InvoicePdfBytes, $"{baseName}.pdf");
      using (var stream = new MemoryStream(zipBytes))
      using (var attachment = new Attachment(stream, $"{baseName}.zip", "application/zip")) {
        await SendMailMessage(
          new PaymentFailedHtml() { Model = model },
          new PaymentFailedPlain() { Model = model },
          $"Payment failed for {model.GitHubUserName}",
          attachment);
      }
    }

    public async Task PaymentRefunded(PaymentRefundedMailMessage model) {
      var baseName = $"ship-credit-{model.CreditNoteDate.ToString("yyyy-MM-dd")}";
      var zipBytes = ZipBytesForPdf(model.CreditNotePdfBytes, $"{baseName}.pdf");
      using (var stream = new MemoryStream(zipBytes))
      using (var attachment = new Attachment(stream, $"{baseName}.zip", "application/zip")) {
        await SendMailMessage(
          new PaymentRefundedHtml() { Model = model },
          new PaymentRefundedPlain() { Model = model },
          $"Payment refunded for {model.GitHubUserName}",
          attachment);
      }
    }

    public async Task PurchasePersonal(PurchasePersonalMailMessage model) {
      var baseName = $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}";
      var zipBytes = ZipBytesForPdf(model.InvoicePdfBytes, $"{baseName}.pdf");
      using (var stream = new MemoryStream(zipBytes))
      using (var attachment = new Attachment(stream, $"{baseName}.zip", "application/zip")) {
        await SendMailMessage(
          new PurchasePersonalHtml() { Model = model },
          new PurchasePersonalPlain() { Model = model },
          $"Ship subscription for {model.GitHubUserName}", attachment);
      }
    }

    public async Task PurchaseOrganization(PurchaseOrganizationMailMessage model) {
      var baseName = $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}";
      var zipBytes = ZipBytesForPdf(model.InvoicePdfBytes, $"{baseName}.pdf");
      using (var stream = new MemoryStream(zipBytes))
      using (var attachment = new Attachment(stream, $"{baseName}.zip", "application/zip")) {
        await SendMailMessage(
          new PurchaseOrganizationHtml() { Model = model },
          new PurchaseOrganizationPlain() { Model = model },
          $"Ship subscription for {model.GitHubUserName}",
          attachment);
      }
    }

    public async Task PaymentSucceededPersonal(PaymentSucceededPersonalMailMessage model) {
      var baseName = $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}";
      var zipBytes = ZipBytesForPdf(model.InvoicePdfBytes, $"{baseName}.pdf");
      using (var stream = new MemoryStream(zipBytes))
      using (var attachment = new Attachment(stream, $"{baseName}.zip", "application/zip")) {
        await SendMailMessage(
          new PaymentSucceededPersonalHtml() { Model = model },
          new PaymentSucceededPersonalPlain() { Model = model },
          $"Payment receipt for {model.GitHubUserName}",
          attachment);
      }
    }

    public async Task PaymentSucceededOrganization(PaymentSucceededOrganizationMailMessage model) {
      var baseName = $"ship-invoice-{model.InvoiceDate.ToString("yyyy-MM-dd")}";
      var zipBytes = ZipBytesForPdf(model.InvoicePdfBytes, $"{baseName}.pdf");
      using (var stream = new MemoryStream(zipBytes))
      using (var attachment = new Attachment(stream, $"{baseName}.zip", "application/zip")) {
        await SendMailMessage(
          new PaymentSucceededOrganizationHtml() { Model = model },
          new PaymentSucceededOrganizationPlain() { Model = model },
          $"Payment receipt for {model.GitHubUserName}",
          attachment);
      }
    }
  }
}
