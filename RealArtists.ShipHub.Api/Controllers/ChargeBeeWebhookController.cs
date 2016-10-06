namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Linq;
  using ChargeBee.Models;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Newtonsoft.Json;
  using QueueClient;

  public class ChargeBeeWebhookCustomer {
    public string Id { get; set; }
    public long ResourceVersion { get; set; }
  }

  public class ChargeBeeWebhookSubscription {
    public long ActivatedAt { get; set; }
    public string Status { get; set; }
    public long? TrialEnd { get; set; }
    public long ResourceVersion { get; set; }
  }

  public class ChargeBeeWebhookInvoiceLineItem {
    public string EntityType { get; set; }
    public string EntityId { get; set; }
    public long DateFrom { get; set; }
  }

  public class ChargeBeeWebhookInvoice {
    public string Id { get; set; }
    public string CustomerId { get; set; }
    public string Status { get; set; }
    public IEnumerable<ChargeBeeWebhookInvoiceLineItem> LineItems { get; set; }
  }

  public class ChargeBeeWebhookContent {
    public ChargeBeeWebhookCustomer Customer { get; set; }
    public ChargeBeeWebhookSubscription Subscription { get; set; }
    public ChargeBeeWebhookInvoice Invoice { get; set; }
  }

  public class ChargeBeeWebhookPayload {
    public ChargeBeeWebhookContent Content { get; set; }
    public string EventType { get; set; }
  }

  [AllowAnonymous]
  public class ChargeBeeWebhookController : ShipHubController {

    private IShipHubQueueClient _queueClient;

    public ChargeBeeWebhookController(IShipHubQueueClient queueClient) {
      _queueClient = queueClient;
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("chargebee")]
    public async Task<IHttpActionResult> HandleHook() {
      var payloadString = await Request.Content.ReadAsStringAsync();
      var payloadBytes = Encoding.UTF8.GetBytes(payloadString);
      var payload = JsonConvert.DeserializeObject<ChargeBeeWebhookPayload>(payloadString, GitHubSerialization.JsonSerializerSettings);

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

      return Ok();
    }

    public async Task HandleSubscriptionStateChange(ChargeBeeWebhookPayload payload) {
      var regex = new Regex(@"^(user|org)-(\d+)$");
      var matches = regex.Match(payload.Content.Customer.Id);
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
    }

    private static DateTimeOffset DateTimeOffsetFloor(DateTimeOffset date) {
      return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task HandlePendingInvoiceCreated(ChargeBeeWebhookPayload payload) {
      var regex = new Regex(@"^(user|org)-(\d+)$");
      var matches = regex.Match(payload.Content.Invoice.CustomerId);
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