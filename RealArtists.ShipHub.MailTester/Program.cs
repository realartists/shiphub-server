namespace RealArtists.ShipHub.MailTester {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using Mail;
  using Mail.Views;

  class Program {
    static void SendEmails(string toAddress, string toName, string githubUsername, bool includeHtmlVersion) {
      var dummyInvoiceUrl = "https://www.realartists.com/billing/invoice/123/ship-invoice-yourname-2016-12-01.pdf";

      var mailer = new ShipHubMailer();
      mailer.IncludeHtmlView = includeHtmlVersion;

      mailer.PurchasePersonal(
        new Mail.Models.PurchasePersonalMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          BelongsToOrganization = true,
          WasGivenTrialCredit = true,
          InvoicePdfUrl = dummyInvoiceUrl,
        }).Wait();

      mailer.PurchaseOrganization(
        new Mail.Models.PurchaseOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoicePdfUrl = dummyInvoiceUrl,
        }).Wait();

      mailer.PaymentSucceededPersonal(
        new Mail.Models.PaymentSucceededPersonalMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoicePdfUrl = dummyInvoiceUrl,
          PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
            PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
            LastCardDigits = "5678",
          },
          AmountPaid = 9.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
        }).Wait();

      // Version with 0 active users
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoicePdfUrl = dummyInvoiceUrl,
          PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
            PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
            LastCardDigits = "5678",
          },
          AmountPaid = 5.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 0,
          PreviousMonthActiveUsersSample = new string[0],
        }).Wait();
      // Version with 1 active user
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoicePdfUrl = dummyInvoiceUrl,
          PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
            PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
            LastCardDigits = "5678",
          },
          AmountPaid = 5.00,
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 1,
          PreviousMonthActiveUsersSample = new string[] { "user_1" },
        }).Wait();
      // Version with 25 active users
      mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = githubUsername,
          ToAddress = toAddress,
          ToName = toName,
          InvoicePdfUrl = dummyInvoiceUrl,
          PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
            PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
            LastCardDigits = "5678",
          },
          AmountPaid = 5.00 + (5 * 24),
          ServiceThroughDate = new DateTimeOffset(2016, 06, 01, 0, 0, 0, TimeSpan.Zero),
          PreviousMonthStart = new DateTimeOffset(2016, 05, 01, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
          PreviousMonthActiveUsersCount = 25,
          PreviousMonthActiveUsersSample = Enumerable.Range(1, 20).Select(x => "user_" + x).ToArray(),
        }).Wait();

      mailer.PaymentRefunded(new Mail.Models.PaymentRefundedMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        CreditNotePdfUrl = dummyInvoiceUrl,
        AmountRefunded = 9.00,
        PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
          PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
          LastCardDigits = "5678",
        },
      }).Wait();

      // Payment failed, but we'll try to retry later.
      mailer.PaymentFailed(new Mail.Models.PaymentFailedMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        InvoicePdfUrl = dummyInvoiceUrl,
        Amount = 9.00,
        PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
          PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
          LastCardDigits = "5678",
        },
        ErrorText = "Insufficient funds",
        NextRetryDate = new DateTimeOffset(2016, 05, 05, 0, 0, 0, TimeSpan.Zero),
        UpdatePaymentMethodUrl = "https://www.chargebee.com",
      }).Wait();

      // Payment failed, no more retries, and service is cancelled.
      mailer.PaymentFailed(new Mail.Models.PaymentFailedMailMessage() {
        GitHubUserName = githubUsername,
        ToAddress = toAddress,
        ToName = toName,
        InvoicePdfUrl = dummyInvoiceUrl,
        Amount = 9.00,
        PaymentMethodSummary = new Mail.Models.PaymentMethodSummary() {
          PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
          LastCardDigits = "5678",
        },
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
