using System;
using System.IO;
using RealArtists.ShipHub.Mail;

namespace RealArtists.ShipHub.EmailTester {
  class Program {
    static void Main(string[] args) {

      var toAddresses = new[] {
        "fred@realartists.com",
        "fpotter@gmail.com",
      };
      var toName = "Fred Potter";

      foreach (var toAddress in toAddresses) {
        var dummyInvoicePdfBytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DummyInvoice.pdf"));
        new ShipHubMailer().PurchasePersonal(
          new Mail.Models.PurchasePersonalMailMessage() {
            ToAddress = toAddress,
            ToName = toName,
            BelongsToOrganization = true,
            WasGivenTrialCredit = true,
            InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
            InvoicePdfBytes = dummyInvoicePdfBytes,
          }).Wait();

        new ShipHubMailer().PurchaseOrganization(
          new Mail.Models.PurchaseOrganizationMailMessage() {
            ToAddress = toAddress,
            ToName = toName,
            InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
            InvoicePdfBytes = dummyInvoicePdfBytes,
          }).Wait();
      }
    }
  }
}
