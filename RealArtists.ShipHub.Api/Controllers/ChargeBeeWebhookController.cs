namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Mail;
  using Newtonsoft.Json;
  using QueueClient;
  using cb = ChargeBee;

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
    public string CustomerId { get; set; }
    public string Id { get; set; }
    public long Date { get; set; }
  }

  public class ChargeBeeWebhookSubscription {
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
    public string PaymentMethod { get; set; }
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
    private cb.ChargeBeeApi _chargeBee;

    public ChargeBeeWebhookController(IShipHubConfiguration configuration, IShipHubQueueClient queueClient, IShipHubMailer mailer, cb.ChargeBeeApi chargeBee) {
      _configuration = configuration;
      _queueClient = queueClient;
      _mailer = mailer;
      _chargeBee = chargeBee;
    }

    private async Task<string> InvoiceUrl(ChargeBeeWebhookPayload payload) {
      var invoiceId = payload.Content.Invoice.Id;
      var githubUserName = await GitHubUserNameFromWebhookPayload(payload);
      var filename = $"ship-invoice-{githubUserName}-{DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.Date).ToString("yyyy-MM-dd")}.pdf";
      var signature = BillingController.CreateSignature(invoiceId, invoiceId);
      var downloadUrl = $"https://{_configuration.ApiHostName}/billing/invoice/{invoiceId}/{signature}/{filename}";
      return downloadUrl;
    }

    private async Task<string> CreditNoteUrl(ChargeBeeWebhookPayload payload) {
      var creditNoteId = payload.Content.CreditNote.Id;
      var githubUserName = await GitHubUserNameFromWebhookPayload(payload);
      var filename = $"ship-credit-{githubUserName}-{DateTimeOffset.FromUnixTimeSeconds(payload.Content.CreditNote.Date).ToString("yyyy-MM-dd")}.pdf";
      var signature = BillingController.CreateSignature(creditNoteId, creditNoteId);
      var downloadUrl = $"https://{_configuration.ApiHostName}/billing/credit/{creditNoteId}/{signature}/{filename}";
      return downloadUrl;
    }

    private async Task<string> GitHubUserNameFromWebhookPayload(ChargeBeeWebhookPayload payload) {
      // Most events include the customer portion which gives us the GitHub username.
      if (payload.Content.Customer?.GitHubUserName != null) {
        return payload.Content.Customer?.GitHubUserName;
      } else {
        // Invoice events (and maybe others, TBD) don't include the Customer portion so
        // we have to find the customer id in another section.
        var candidates = new[] {
          payload.Content.Invoice?.CustomerId,
          payload.Content.CreditNote?.CustomerId,
        };
        var customerId = candidates.SkipWhile(string.IsNullOrEmpty).FirstOrDefault();
        if (customerId != null) {
          var accountId = ChargeBeeUtilities.AccountIdFromCustomerId(customerId);
          using (var context = new ShipHubContext()) {
            var account = await context.Accounts.SingleOrDefaultAsync(x => x.Id == accountId);
            return account?.Login;
          }
        } else {
          return null;
        }
      }
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
    /// <param name="gitHubUserName">GitHub user or organization name</param>
    /// <returns>True if we should reject this webhook event</returns>
    public virtual async Task<bool> ShouldIgnoreWebhook(ChargeBeeWebhookPayload payload) {
      if (_configuration.ChargeBeeWebhookIncludeOnlyList != null
        && !_configuration.ChargeBeeWebhookIncludeOnlyList.Contains(await GitHubUserNameFromWebhookPayload(payload))) {
        return true;
      }

      if (_configuration.ChargeBeeWebhookExcludeList != null
        && _configuration.ChargeBeeWebhookExcludeList.Contains(await GitHubUserNameFromWebhookPayload(payload))) {
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

      if (await ShouldIgnoreWebhook(payload)) {
        return Ok($"Ignoring webhook because customer's GitHub username is on the exclude list, or not on the include list.");
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

      if (
        (payload.EventType == "subscription_activated" ||
         payload.EventType == "subscription_reactivated") &&
        payload.Content.Subscription.Status == "active" &&
        payload.Content.Subscription.PlanId == "personal") {
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

    private static string GetPaymentMethodUpdateUrl(IShipHubConfiguration configuration, string customerId) {
      var accountId = ChargeBeeUtilities.AccountIdFromCustomerId(customerId);

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

    private Mail.Models.PaymentMethodSummary PaymentMethodSummary(ChargeBeeWebhookTransaction transaction) {
      if (transaction.PaymentMethod == "paypal_express_checkout") {
        return new Mail.Models.PaymentMethodSummary() {
          PaymentMethod = Mail.Models.PaymentMethod.PayPal,
        };
      } else if (transaction.PaymentMethod == "card") {
        return new Mail.Models.PaymentMethodSummary() {
          PaymentMethod = Mail.Models.PaymentMethod.CreditCard,
          LastCardDigits = transaction.MaskedCardNumber.Replace("*", ""),
        };
      } else {
        throw new NotSupportedException();
      }
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
      var updateUrl = GetPaymentMethodUpdateUrl(_configuration, payload.Content.Customer.Id);

      var message = new Mail.Models.PaymentFailedMailMessage() {
        GitHubUserName = payload.Content.Customer.GitHubUserName,
        ToAddress = payload.Content.Customer.Email,
        ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
        Amount = payload.Content.Transaction.Amount / 100.0,
        InvoicePdfUrl = await InvoiceUrl(payload),
        PaymentMethodSummary = PaymentMethodSummary(payload.Content.Transaction),
        ErrorText = payload.Content.Transaction.ErrorText,
        UpdatePaymentMethodUrl = updateUrl,
      };

      if (payload.Content.Invoice.NextRetryAt != null) {
        message.NextRetryDate = DateTimeOffset.FromUnixTimeSeconds(payload.Content.Invoice.NextRetryAt.Value);
      }

      await _mailer.PaymentFailed(message);
    }

    public async Task SendPaymentRefundedMessage(ChargeBeeWebhookPayload payload) {
      await _mailer.PaymentRefunded(new Mail.Models.PaymentRefundedMailMessage() {
        GitHubUserName = payload.Content.Customer.GitHubUserName,
        ToAddress = payload.Content.Customer.Email,
        ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
        AmountRefunded = payload.Content.CreditNote.AmountRefunded / 100.0,
        CreditNotePdfUrl = await CreditNoteUrl(payload),
        PaymentMethodSummary = PaymentMethodSummary(payload.Content.Transaction),
      });
    }

    public async Task SendPaymentSucceededPersonalMessage(ChargeBeeWebhookPayload payload) {
      var planLineItem = payload.Content.Invoice.LineItems.Single(x => x.EntityType == "plan");

      await _mailer.PaymentSucceededPersonal(
        new Mail.Models.PaymentSucceededPersonalMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          InvoicePdfUrl = await InvoiceUrl(payload),
          AmountPaid = payload.Content.Invoice.AmountPaid / 100.0,
          ServiceThroughDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateTo),
          PaymentMethodSummary = PaymentMethodSummary(payload.Content.Transaction),
        });
    }

    public async Task SendPaymentSucceededOrganizationMessage(ChargeBeeWebhookPayload payload) {
      var accountId = ChargeBeeUtilities.AccountIdFromCustomerId(payload.Content.Customer.Id);

      var planLineItem = payload.Content.Invoice.LineItems.Single(x => x.EntityType == "plan");

      var newInvoiceStartDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateFrom);
      var previousMonthStart = DateTimeOffsetFloor(newInvoiceStartDate.AddMonths(-1));
      var previousMonthEnd = DateTimeOffsetFloor(newInvoiceStartDate.AddDays(-1));

      int activeUsersCount;
      string[] activeUsersSample;
      using (var context = new ShipHubContext()) {
        activeUsersCount = await context.Usage
          .Where(x => (
            x.Date >= previousMonthStart &&
            x.Date <= previousMonthEnd &&
            context.OrganizationAccounts
              .Where(y => y.OrganizationId == accountId)
              .Select(y => y.UserId)
              .Contains(x.AccountId)))
          .Select(x => x.AccountId)
          .Distinct()
          .CountAsync();

        activeUsersSample = await context.Usage
          .Where(x => (
            x.Date >= previousMonthStart &&
            x.Date <= previousMonthEnd &&
            context.OrganizationAccounts
              .Where(y => y.OrganizationId == accountId)
              .Select(y => y.UserId)
              .Contains(x.AccountId)))
          .Select(x => x.Account.Login)
          .OrderBy(x => x)
          .Distinct()
          .Take(20)
          .ToArrayAsync();
      }

      await _mailer.PaymentSucceededOrganization(
        new Mail.Models.PaymentSucceededOrganizationMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          InvoicePdfUrl = await InvoiceUrl(payload),
          ServiceThroughDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateTo),
          PreviousMonthActiveUsersCount = activeUsersCount,
          PreviousMonthActiveUsersSample = activeUsersSample,
          PreviousMonthStart = previousMonthStart,
          AmountPaid = payload.Content.Invoice.AmountPaid / 100.0,
          PaymentMethodSummary = PaymentMethodSummary(payload.Content.Transaction),
        });
    }

    public async Task SendPurchasePersonalMessage(ChargeBeeWebhookPayload payload) {
      ChargeBeeUtilities.ParseCustomerId(payload.Content.Customer.Id, out var accountType, out var accountId);

      if (accountType != "user") {
        // "activated" only happens on transition from trial -> active, and we only do trials
        // for personal subscriptions.
        throw new Exception("subscription_activated should only happen on personal/user subscriptions");
      }

      var belongsToOrganization = false;

      using (var context = new ShipHubContext()) {
        belongsToOrganization = (await context.OrganizationAccounts.CountAsync(x => x.UserId == accountId)) > 0;
      }

      var wasGivenTrialCredit = payload.Content.Invoice.Discounts?
        .Count(x => x.EntityType == "document_level_coupon" && x.EntityId.StartsWith("trial_days_left")) > 0;

      await _mailer.PurchasePersonal(
        new Mail.Models.PurchasePersonalMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          BelongsToOrganization = belongsToOrganization,
          WasGivenTrialCredit = wasGivenTrialCredit,
          InvoicePdfUrl = await InvoiceUrl(payload),
        });
    }

    public async Task SendPurchaseOrganizationMessage(ChargeBeeWebhookPayload payload) {
      var accountId = ChargeBeeUtilities.AccountIdFromCustomerId(payload.Content.Customer.Id);

      await _mailer.PurchaseOrganization(
        new Mail.Models.PurchaseOrganizationMailMessage() {
          GitHubUserName = payload.Content.Customer.GitHubUserName,
          ToAddress = payload.Content.Customer.Email,
          ToName = payload.Content.Customer.FirstName + " " + payload.Content.Customer.LastName,
          InvoicePdfUrl = await InvoiceUrl(payload),
        });
    }

    public async Task HandleSubscriptionStateChange(ChargeBeeWebhookPayload payload) {
      var accountId = ChargeBeeUtilities.AccountIdFromCustomerId(payload.Content.Customer.Id);
      ChangeSummary changes;
      var tasks = new List<Task>();

      using (var context = new ShipHubContext()) {
        var sub = await context.Subscriptions
         .AsNoTracking()
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
        } else {
          incomingVersion = payload.Content.Subscription.ResourceVersion;
        }

        if (incomingVersion <= sub.Version) {
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

        changes = await context.BulkUpdateSubscriptions(new[] {
          new SubscriptionTableType() {
            AccountId = sub.AccountId,
            State = sub.StateName,
            TrialEndDate = sub.TrialEndDate,
            Version = sub.Version,
          }
        });

        var afterState = sub.State;
        if (afterState != beforeState && sub.Account is Organization) {
          // For all users associated with this org and that have logged into Ship
          // (i.e., they have a Subscription record), go re-evaluate whether the
          // user should have a complimentary personal subscription.
          var orgAccountIds = context.OrganizationAccounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == sub.AccountId && x.User.Subscription != null)
            .Select(x => x.UserId)
            .ToArray();
          tasks.AddRange(orgAccountIds.Select(x => _queueClient.BillingUpdateComplimentarySubscription(x)));
        }
      }

      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }

      await Task.WhenAll(tasks);
    }

    private static DateTimeOffset DateTimeOffsetFloor(DateTimeOffset date) {
      return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task HandlePendingInvoiceCreated(ChargeBeeWebhookPayload payload) {
      var accountId = ChargeBeeUtilities.AccountIdFromCustomerId(payload.Content.Invoice.CustomerId);
      var planLineItem = payload.Content.Invoice.LineItems.Single(x => x.EntityType == "plan");

      if (planLineItem.EntityId == "organization") {
        // Calculate the number of active users during the previous month, and then
        // attach extra charges to this month's invoice.  So, for organizations, the
        // base charge on every invoice is for the coming month, but the metered
        // component is always for the trailing month.
        var newInvoiceStartDate = DateTimeOffset.FromUnixTimeSeconds(planLineItem.DateFrom);
        var previousMonthStart = DateTimeOffsetFloor(newInvoiceStartDate.AddMonths(-1));
        var previousMonthEnd = DateTimeOffsetFloor(newInvoiceStartDate.AddDays(-1));

        int activeUsers;
        using (var context = new ShipHubContext()) {
          activeUsers = await context.Usage
            .AsNoTracking()
            .Where(x => (
              x.Date >= previousMonthStart &&
              x.Date <= previousMonthEnd &&
              context.OrganizationAccounts
                .Where(y => y.OrganizationId == accountId)
                .Select(y => y.UserId)
                .Contains(x.AccountId)))
            .Select(x => x.AccountId)
            .Distinct()
            .CountAsync();
        }

        if (activeUsers > 5) {
          await _chargeBee.Invoice.AddAddonCharge(payload.Content.Invoice.Id)
            .AddonId("additional-seats")
            .AddonQuantity(Math.Max(activeUsers - 5, 0))
            .Request();
        }
      }

      await _chargeBee.Invoice.Close(payload.Content.Invoice.Id).Request();
    }
  }
}
