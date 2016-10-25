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
  using RealArtists.ShipHub.Mail.Models;

  public interface IShipHubMailer {
    Task PurchasePersonal(PurchasePersonalMailMessage model);
    Task PurchaseOrganization(PurchaseOrganizationMailMessage model);
  }

  public class ShipHubMailer : IShipHubMailer {
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

      var message = new MailMessage(
        new MailAddress("support@realartists.com", "Ship"),
        new MailAddress(model.ToAddress, model.ToName));
      message.Subject = subject;
      message.Body = text;

      var shipLogo = File.ReadAllBytes(Path.Combine(GetBaseDirectory(), "ShipLogo.png"));
      var htmlView = new AlternateView(
        new MemoryStream(UTF8Encoding.Default.GetBytes(htmlProcessed)),
        new ContentType("text/html"));
      htmlView.LinkedResources.Add(new LinkedResource(new MemoryStream(shipLogo)) {
        ContentType = new ContentType("image/png"),
        ContentId = "ShipLogo.png",
      });
      message.AlternateViews.Add(htmlView);

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
  }
}

