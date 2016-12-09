namespace RealArtists.ShipHub.MailTester {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using Mail;
  using Mail.Views;

  class Program {
    static void SendEmails(string toAddress, string toName, string githubUsername, bool includeHtmlVersion) {
      var dummyInvoicePdfBytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DummyInvoice.pdf"));

      var mailer = new ShipHubMailer();
      mailer.IncludeHtmlView = includeHtmlVersion;

      mailer.PurchasePersonal(
        new Mail.Models.PurchasePersonalMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          BelongsToOrganization = true,
          WasGivenTrialCredit = true,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
        }).Wait();

      mailer.PurchaseOrganization(
        new Mail.Models.PurchaseOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
        }).Wait();

      mailer.PaymentSucceededPersonal(
        new Mail.Models.PaymentSucceededPersonalMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
          LastCardDigits = "1234",
          AmountPaid = 9.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
        }).Wait();

      // Version with <= 5 active users
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
          LastCardDigits = "1234",
          AmountPaid = 25.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 4,
          PreviousMonthActiveUsersSample = Enumerable.Range(1, 4).Select(x => "user_" + x).ToArray()
        }).Wait();
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
          InvoicePdfBytes = dummyInvoicePdfBytes,
          LastCardDigits = "1234",
          AmountPaid = 25.00 + (9 * 25),
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 25,
          PreviousMonthActiveUsersSample = Enumerable.Range(1, 20).Select(x => "user_" + x).ToArray(),
        }).Wait();

      mailer.PaymentRefunded(new Mail.Models.PaymentRefundedMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        CreditNoteDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
        CreditNotePdfBytes = dummyInvoicePdfBytes,
        AmountRefunded = 9.00,
        LastCardDigits = "5678",
      }).Wait();

      // Payment failed, but we'll try to retry later.
      mailer.PaymentFailed(new Mail.Models.PaymentFailedMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
        InvoicePdfBytes = dummyInvoicePdfBytes,
        Amount = 9.00,
        LastCardDigits = "5678",
        ErrorText = "Insufficient funds",
        NextRetryDate = new DateTimeOffset(2016, 05, 05, 0, 0, 0, TimeSpan.Zero),
        UpdatePaymentMethodUrl = "https://www.chargebee.com",
      }).Wait();

      // Payment failed, no more retries, and service is cancelled.
      mailer.PaymentFailed(new Mail.Models.PaymentFailedMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        InvoiceDate = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero),
        InvoicePdfBytes = dummyInvoicePdfBytes,
        Amount = 9.00,
        LastCardDigits = "5678",
        ErrorText = "Insufficient funds",
        NextRetryDate = null,
      }).Wait();

      // Card has already expired.
      mailer.CardExpiryReminder(new Mail.Models.CardExpiryReminderMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        LastCardDigits = "5678",
        AlreadyExpired = true,
        ExpiryMonth = 9,
        ExpiryYear = 2016,
        UpdatePaymentMethodUrl = "https://pretend.com/this/is/right",
      }).Wait();

      // Card will expire.
      mailer.CardExpiryReminder(new Mail.Models.CardExpiryReminderMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        LastCardDigits = "5678",
        AlreadyExpired = false,
        ExpiryMonth = 9,
        ExpiryYear = 2016,
        UpdatePaymentMethodUrl = "https://pretend.com/this/is/right",
      }).Wait();

      mailer.CancellationScheduled(new Mail.Models.CancellationScheduledMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        CurrentTermEnd = new DateTimeOffset(2016, 12, 01, 0, 0, 0, TimeSpan.Zero),
      }).Wait();
    }

    static void Main(string[] args) {
      var toAddresses = new List<string>(args);
      if (toAddresses.Count == 0) {
        toAddresses.AddRange(new[] {
          "fred@realartists.com",
          "fpotter@gmail.com",
        });
      }
      var toName = "Fred Potter";
      var githubUsername = "fpotter";

      foreach (var toAddress in toAddresses) {
        SendEmails(toAddress, toName, githubUsername, includeHtmlVersion: true);
        SendEmails(toAddress, toName, githubUsername, includeHtmlVersion: false);
      }
    }
  }
}
