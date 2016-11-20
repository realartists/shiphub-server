namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ChargeBee.Models;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Mail;
  using Microsoft.Azure;
  using Newtonsoft.Json;
  using QueueClient;

  public class ChargeBeeWebhookCard {
    public long ExpiryMonth { get; set; }
    public long ExpiryYear { get; set; }
    public string Last4 { get; set; }
  }

  public class ChargeBeeWebhookCustomer {
    public string FirstName { get; set; }
    [JsonProperty(PropertyName = "cf_github_username")]
    public string GitHubUserName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Id { get; set; }
    public long ResourceVersion { get; set; }
  }

  public class ChargeBeeWebhookCreditNote {
    public int AmountRefunded { get; set; }
    public string Id { get; set; }
    public long Date { get; set; }
  }

  public class ChargeBeeWebhookSubscription {
    public long ActivatedAt { get; set; }
    public long CurrentTermEnd { get; set; }
    public string PlanId { get; set; }
    public string Status { get; set; }
    public long? TrialEnd { get; set; }
    public long ResourceVersion { get; set; }
  }

  public class ChargeBeeWebhookInvoiceLineItem {
    public string EntityType { get; set; }
    public string EntityId { get; set; }
    public long DateFrom { get; set; }
    public long DateTo { get; set; }
  }

  public class ChargeBeeWebhookInvoiceDiscount {
    public string EntityType { get; set; }
    public string EntityId { get; set; }
  }

  public class ChargeBeeWebhookInvoice {
    public int AmountPaid { get; set; }
    public string Id { get; set; }
    public string CustomerId { get; set; }
    public long Date { get; set; }
    public bool FirstInvoice { get; set; }
    public IEnumerable<ChargeBeeWebhookInvoiceLineItem> LineItems { get; set; }
    public IEnumerable<ChargeBeeWebhookInvoiceDiscount> Discounts { get; set; }
    public long? NextRetryAt { get; set; }
  }

  public class ChargeBeeWebhookTransaction {
    public long Amount { get; set; }
    public string ErrorText { get; set; }
    public string MaskedCardNumber { get; set; }
  }

  public class ChargeBeeWebhookContent {
    public ChargeBeeWebhookCard Card { get; set; }
    public ChargeBeeWebhookCustomer Customer { get; set; }
    public ChargeBeeWebhookSubscription Subscription { get; set; }
    public ChargeBeeWebhookInvoice Invoice { get; set; }
    public ChargeBeeWebhookTransaction Transaction { get; set; }
    public ChargeBeeWebhookCreditNote CreditNote { get; set; }
  }

  public class ChargeBeeWebhookPayload {
    public ChargeBeeWebhookContent Content { get; set; }
    public string EventType { get; set; }
  }

  [AllowAnonymous]
  public class ChargeBeeWebhookController : ShipHubController {
    private IShipHubConfiguration _configuration;
    private IShipHubQueueClient _queueClient;
    private IShipHubMailer _mailer;

    public ChargeBeeWebhookController(IShipHubConfiguration configuration, IShipHubQueueClient queueClient, IShipHubMailer mailer) {
      _configuration = configuration;
      _queueClient = queueClient;
      _mailer = mailer;
    }

    public async virtual Task<byte[]> GetInvoicePdfBytes(string invoiceId) {
      var downloadUrl = Invoice.Pdf(invoiceId).Request().Download.DownloadUrl;

      byte[] invoiceBytes;

      using (var client = new WebClient()) {
        invoiceBytes = await client.DownloadDataTaskAsync(downloadUrl);
      }

      return invoiceBytes;
    }

    public async virtual Task<byte[]> GetCreditNotePdfBytes(string creditNoteId) {
      var downloadUrl = CreditNote.Pdf(creditNoteId).Request().Download.DownloadUrl;

      byte[] invoiceBytes;

      using (var client = new WebClient()) {
        invoiceBytes = await client.DownloadDataTaskAsync(downloadUrl);
      }

      return invoiceBytes;
    }

    /// <summary>
    /// When testing ChargeBee webhooks on your local server, you want to prevent shiphub-dev
    /// from handling the same hook.  Sometimes it's harmless for shiphub-dev and your local server
    /// to process the same hook twice.  Other times, it's a race condition because handling that hook
    /// changes some ChargeBee state.
    /// 
    /// If you're testing ChargeBee webhooks locally, do this --
    /// 
    /// In the app settings for shiphub-dev, add the github username to "ChargeBeeWebhookExcludeList" --
    /// https://portal.azure.com/#resource/subscriptions/b9f28aae-2074-4097-b5ce-ec28f68c4981/resourceGroups/ShipHub-Dev/providers/Microsoft.Web/sites/shiphub-dev/application
    /// 
    /// In your Secret.AppSettings.config, add the following -- <![CDATA[
    ///   <add key="ChargeBeeWebhookIncludeList" value="some_github_username"/>
    /// ]]>
    /// 
    /// Dev will ignore hooks for your user and your local server will only process
    /// hooks for your user.
    /// </summary>
    /// <param name="gitHubUserName">Github user or organization name</param>
    /// <returns>True if we should reject this webhook event</returns>
    private bool ShouldIgnoreWebhook(string gitHubUserName) {
      if (_configuration.ChargeBeeWebhookIncludeOnlyList != null
        && !_configuration.ChargeBeeWebhookIncludeOnlyList.Contains(gitHubUserName)) {
        return true;
      }

      if (_configuration.ChargeBeeWebhookExcludeList != null
        && _configuration.ChargeBeeWebhookExcludeList.Contains(gitHubUserName)) {
        return true;
      }

      return false;
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("chargebee/{secret}")]
    public async Task<IHttpActionResult> HandleHook(string secret) {
      if (secret != _configuration.ChargeBeeWebhookSecret) {
        return BadRequest("Invalid secret.");
      }

      var payloadString = await Request.Content.ReadAsStringAsync();
      var payloadBytes = Encoding.UTF8.GetBytes(payloadString);
      var payload = JsonConvert.DeserializeObject<ChargeBeeWebhookPayload>(payloadString, GitHubSerialization.JsonSerializerSettings);

      if (ShouldIgnoreWebhook(payload.Content.Customer.GitHubUserName)) {
        return BadRequest($"Rejecting webhook because username '{payload.Content.Customer.GitHubUserName}' is on the exclude list, or not on the include list.");
      }

      switch (payload.EventType) {
        case "subscription_activated":
        case "subscription_reactivated":
        case "subscription_started":
        case "subscription_cancelled":
        case "subscription_changed":
        case "subscription_deleted":
        case "subscription_scheduled_cancellation_removed":
        case "subscription_created":
        case "customer_deleted":
          await HandleSubscriptionStateChange(payload);
          break;
        case "pending_invoice_created":
          await HandlePendingInvoiceCreated(payload);
          break;
        default:
          break;
      }

      if (payload.EventType == "subscription_activated") {
        await SendPurchasePersonalMessage(payload);
      } else if (
        payload.EventType == "subscription_created" &&
        payload.Content.Subscription.PlanId == "organization") {
        await SendPurchaseOrganizationMessage(payload);
      } else if (
        payload.EventType == "payment_succeeded" &&
        !payload.Content.Invoice.FirstInvoice &&
        payload.Content.Subscription.PlanId == "personal") {
        await SendPaymentSucceededPersonalMessage(payload);
      } else if (
        payload.EventType == "payment_succeeded" &&
        !payload.Content.Invoice.FirstInvoice &&
        payload.Content.Subscription.PlanId == "organization") {
        await SendPaymentSucceededOrganizationMessage(payload);
      } else if (payload.EventType == "payment_refunded") {
        await SendPaymentRefundedMessage(payload);
      } else if (payload.EventType == "payment_failed") {
        await SendPaymentFailedMessage(payload);
      } else if (payload.EventType == "card_expired" || payload.EventType == "card_expiry_reminder") {
        await SendCardExpiryReminderMessage(payload);
      } else if (payload.EventType == "subscription_cancellation_scheduled") {
        await SendCancellationScheduled(payload);
      }

      return Ok();
    }

    private static Regex CustomerIdRegex { get; } = new Regex(@"^(user|org)-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static string GetPaymentMethodUpdateUrl(IShipHubConfiguration configuration, string customerId) {
      var matches = CustomerIdRegex.Match(customerId);
      var accountId = long.Parse(matches.Groups[2].ToString());

      var apiHostName = configuration.ApiHostName;

      var signature = BillingController.CreateSignature(accountId, accountId);
      var updateUrl = $"https://{apiHostName}/billing/update/{accountId}/{signature}";

      return updateUrl;
    }

    public async Task SendCancellationScheduled(ChargeBeeWebhookPayload payload) {
      var updateUrl = GetPaymentMethodUpdateUrl(_configuration, payload.Content.Customer.Id);

      await _mailer.CancellationScheduled(new Mail.Models.CancellationScheduledMailMessage() {
        GitHubUserName = payload.Content.Customer.GitHubUserName,
        ToAddress = payload.Content.Customer.Email,
        ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
        CurrentTermEnd = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Subscription.CurrentTermEnd),
      });
    }

    public async Task SendCardExpiryReminderMessage(ChargeBeeWebhookPayload payload) {
      var updateUrl = GetPaymentMethodUpdateUrl(_configuration, payload.Content.Customer.Id);

      await _mailer.CardExpiryReminder(new Mail.Models.CardExpiryReminderMailMessage() {
        GitHubUserName = payload.Content.Customer.GitHubUserName,
        ToAddress = payload.Content.Customer.Email,
        ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
        LastCardDigits = payload.Content.Card.Last4,
        UpdatePaymentMethodUrl = updateUrl,
        ExpiryMonth = payload.Content.Card.ExpiryMonth,
        ExpiryYear = payload.Content.Card.ExpiryYear,
        AlreadyExpired = payload.EventType == "card_expired",
      });
    }

    public async Task SendPaymentFailedMessage(ChargeBeeWebhookPayload payload) {
      var pdfBytes = await GetInvoicePdfBytes(payload.Content.Invoice.Id);
      var updateUrl = GetPaymentMethodUpdateUrl(_configuration, payload.Content.Customer.Id);

      var message = new Mail.Models.PaymentFailedMailMessage() {
        GitHubUserName = payload.Content.Customer.GitHubUserName,
        ToAddress = payload.Content.Customer.Email,
        ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
        Amount = payload.Content.Transaction.Amount / 100.0,
        InvoiceDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.Date),
        InvoicePdfBytes = pdfBytes,
        LastCardDigits = payload.Content.Transaction.MaskedCardNumber.Replace("*", ""),
        ErrorText = payload.Content.Transaction.ErrorText,
        UpdatePaymentMethodUrl = updateUrl,
      };

      if (payload.Content.Invoice.NextRetryAt != null) {
        message.NextRetryDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.NextRetryAt.Value);
      }

      await _mailer.PaymentFailed(message);
    }

    public async Task SendPaymentRefundedMessage(ChargeBeeWebhookPayload payload) {
      var pdfBytes = await GetCreditNotePdfBytes(payload.Content.CreditNote.Id);

      await _mailer.PaymentRefunded(new Mail.Models.PaymentRefundedMailMessage() {
        GitHubUserName = payload.Content.Customer.GitHubUserName,
        ToAddress = payload.Content.Customer.Email,
        ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
        AmountRefunded = payload.Content.CreditNote.AmountRefunded / 100.0,
        CreditNoteDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.CreditNote.Date),
        CreditNotePdfBytes = pdfBytes,
        LastCardDigits = payload.Content.Transaction.MaskedCardNumber.Replace("*", ""),
      });
    }

    public async Task SendPaymentSucceededPersonalMessage(ChargeBeeWebhookPayload payload) {
      var invoicePdfBytes = await GetInvoicePdfBytes(payload.Content.Invoice.Id);

      var planLineItem = payload.Content.Invoice.LineItems.Single(x => x.EntityType == "plan");

      await _mailer.PaymentSucceededPersonal(
        new Mail.Models.PaymentSucceededPersonalMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          InvoiceDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.Date),
          InvoicePdfBytes = invoicePdfBytes,
          AmountPaid = payload.Content.Invoice.AmountPaid / 100.0,
          ServiceThroughDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateTo),
          LastCardDigits = payload.Content.Transaction.MaskedCardNumber.Replace("*", ""),
        });
    }

    public async Task SendPaymentSucceededOrganizationMessage(ChargeBeeWebhookPayload payload) {
      var matches = CustomerIdRegex.Match(payload.Content.Customer.Id);
      var accountId = long.Parse(matches.Groups[2].ToString());

      var invoicePdfBytes = await GetInvoicePdfBytes(payload.Content.Invoice.Id);
      var planLineItem = payload.Content.Invoice.LineItems.Single(x => x.EntityType == "plan");

      var newInvoiceStartDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateFrom);
      var previousMonthStart = DateTimeOffsetFloor(newInvoiceStartDate.AddMonths(-1));
      var previousMonthEnd = DateTimeOffsetFloor(newInvoiceStartDate.AddDays(-1));

      var activeUsersCount = await Context.Usage
        .Where(x => (
          x.Date >= previousMonthStart &&
          x.Date <= previousMonthEnd &&
          Context.OrganizationAccounts
            .Where(y => y.OrganizationId == accountId)
            .Select(y => y.UserId)
            .Contains(x.AccountId)))
        .Select(x => x.AccountId)
        .Distinct()
        .CountAsync();

      var activeUsersSample = await Context.Usage
        .Include(x => x.Account)
        .Where(x => (
          x.Date >= previousMonthStart &&
          x.Date <= previousMonthEnd &&
          Context.OrganizationAccounts
            .Where(y => y.OrganizationId == accountId)
            .Select(y => y.UserId)
            .Contains(x.AccountId)))
        .Select(x => x.Account.Login)
        .OrderBy(x => x)
        .Distinct()
        .Take(20)
        .ToArrayAsync();

      await _mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          InvoiceDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.Date),
          InvoicePdfBytes = invoicePdfBytes,
          ServiceThroughDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateTo),
          PreviousMonthActiveUsersCount = activeUsersCount,
          PreviousMonthActiveUsersSample = activeUsersSample,
          PreviousMonthStart = previousMonthStart,
          AmountPaid = payload.Content.Invoice.AmountPaid / 100.0,
          LastCardDigits = payload.Content.Transaction.MaskedCardNumber.Replace("*", ""),
        });
    }

    public async Task SendPurchasePersonalMessage(ChargeBeeWebhookPayload payload) {
      var matches = CustomerIdRegex.Match(payload.Content.Customer.Id);

      if (matches.Groups[1].ToString() != "user") {
        // "activated" only happens on transition from trial -> active, and we only do trials
        // for personal subscriptions.
        throw new Exception("subscription_activated should only happen on personal/user subscriptions");
      }

      var accountId = long.Parse(matches.Groups[2].ToString());

      bool belongsToOrganization = false;

      using (var context = new ShipHubContext()) {
        belongsToOrganization = (await context.OrganizationAccounts.CountAsync(x => x.UserId == accountId)) > 0;
      }

      bool wasGivenTrialCredit = payload.Content.Invoice.Discounts?
        .Count(x => x.EntityType == "document_level_coupon" && x.EntityId.StartsWith("trial_days_left")) > 0;
      var invoicePdfBytes = await GetInvoicePdfBytes(payload.Content.Invoice.Id);

      await _mailer.PurchasePersonal(
        new Mail.Models.PurchasePersonalMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          BelongsToOrganization = belongsToOrganization,
          WasGivenTrialCredit = wasGivenTrialCredit,
          InvoiceDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.Date),
          InvoicePdfBytes = invoicePdfBytes,
        });
    }

    public async Task SendPurchaseOrganizationMessage(ChargeBeeWebhookPayload payload) {
      var matches = CustomerIdRegex.Match(payload.Content.Customer.Id);

      var accountId = long.Parse(matches.Groups[2].ToString());
      var invoicePdfBytes = await GetInvoicePdfBytes(payload.Content.Invoice.Id);

      await _mailer.PurchaseOrganization(
        new Mail.Models.PurchaseOrganizationMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          InvoiceDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.Date),
          InvoicePdfBytes = invoicePdfBytes,
        });
    }

    public async Task HandleSubscriptionStateChange(ChargeBeeWebhookPayload payload) {
      var matches = CustomerIdRegex.Match(payload.Content.Customer.Id);
      var accountId = long.Parse(matches.Groups[2].ToString());

      var sub = await Context.Subscriptions
        .Include(x => x.Account)
        .SingleOrDefaultAsync(x => x.AccountId == accountId);

      if (sub == null) {
        // We only care to update subscriptions we've already sync'ed.  This case often happens
        // in development - e.g., you might be testing subscriptions on your local machine, and
        // chargebee delivers webhook calls to shiphub-dev about subscriptions it doesn't know
        // about yet.
        return;
      }

      long incomingVersion;

      if (payload.EventType == "customer_deleted") {
        incomingVersion = payload.Content.Customer.ResourceVersion;
      } else if (payload.EventType == "subscription_reactivated") {
        // The "resource_version" field on "subscription_reactivatd" events
        // is bogus - ChargeBee says they'll work on a fix.  In the meantime, we
        // can use the "activated_at" column to get a timestamp - it just doesn't
        // have millis resolution.
        incomingVersion = payload.Content.Subscription.ActivatedAt * 1000;
      } else {
        incomingVersion = payload.Content.Subscription.ResourceVersion;
      }

      if (incomingVersion < sub.Version) {
        // We're receiving webhook events out-of-order (which can happen due to re-delivery),
        // so ignore.
        return;
      }

      sub.Version = incomingVersion;
      var beforeState = sub.State;

      if (payload.EventType.Equals("subscription_deleted") ||
          payload.EventType.Equals("customer_deleted")) {
        sub.State = SubscriptionState.NotSubscribed;
        sub.TrialEndDate = null;
      } else {
        switch (payload.Content.Subscription.Status) {
          case "in_trial":
            sub.State = SubscriptionState.InTrial;
            sub.TrialEndDate = DateTimeOffset.FromUnixTimeSeconds((long)payload.Content.Subscription.TrialEnd);
            break;
          case "active":
          case "non_renewing":
          case "future":
            sub.State = SubscriptionState.Subscribed;
            sub.TrialEndDate = null;
            break;
          case "cancelled":
            sub.State = SubscriptionState.NotSubscribed;
            sub.TrialEndDate = null;
            break;
        }
      }

      var afterState = sub.State;

      var recordsUpdated = await Context.SaveChangesAsync();

      if (recordsUpdated > 0) {
        var changes = new ChangeSummary();

        if (sub.Account is Organization) {
          changes.Organizations.Add(accountId);
        } else {
          changes.Users.Add(accountId);
        }

        await _queueClient.NotifyChanges(changes);
      }

      if (afterState != beforeState && sub.Account is Organization) {
        // For all users associated with this org and that have logged into Ship
        // (i.e., they have a Subscription record), go re-evaluate whether the
        // user should have a complimentary personal subscription.
        var orgAccountIds = Context.OrganizationAccounts
          .Where(x => x.OrganizationId == sub.AccountId && x.User.Subscription != null)
          .Select(x => x.UserId)
          .ToArray();
        await Task.WhenAll(orgAccountIds.Select(x => _queueClient.BillingUpdateComplimentarySubscription(x)));
      }
    }

    private static DateTimeOffset DateTimeOffsetFloor(DateTimeOffset date) {
      return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task HandlePendingInvoiceCreated(ChargeBeeWebhookPayload payload) {
      var matches = CustomerIdRegex.Match(payload.Content.Invoice.CustomerId);
      var accountId = long.Parse(matches.Groups[2].ToString());
      var planLineItem = payload.Content.Invoice.LineItems.Single(x => x.EntityType == "plan");

      if (planLineItem.EntityId == "organization") {
        // Calculate the number of active users during the previous month, and then
        // attach extra charges to this month's invoice.  So, for organizations, the
        // base charge on every invoice is for the coming month, but the metered
        // component is always for the trailing month.
        var newInvoiceStartDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateFrom);
        var previousMonthStart = DateTimeOffsetFloor(newInvoiceStartDate.AddMonths(-1));
        var previousMonthEnd = DateTimeOffsetFloor(newInvoiceStartDate.AddDays(-1));

        var activeUsers = await Context.Usage
          .Where(x => (
            x.Date >= previousMonthStart &&
            x.Date <= previousMonthEnd &&
            Context.OrganizationAccounts
              .Where(y => y.OrganizationId == accountId)
              .Select(y => y.UserId)
              .Contains(x.AccountId)))
          .Select(x => x.AccountId)
          .Distinct()
          .CountAsync();

        if (activeUsers > 5) {
          Invoice.AddAddonCharge(payload.Content.Invoice.Id)
            .AddonId("additional-seats")
            .AddonQuantity(Math.Max(activeUsers - 5, 0))
            .Request();
        }
      }

      Invoice.Close(payload.Content.Invoice.Id).Request();
    }
  }
}
