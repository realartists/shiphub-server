namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Data.Entity;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Newtonsoft.Json;
  using QueueClient;

  public class ChargeBeeWebhookCustomer {
    public string Id { get; set; }
  }

  public class ChargeBeeWebhookSubscription {
    public string Status { get; set; }
    public long? TrialEnd { get; set; }
  }

  public class ChargeBeeWebhookContent {
    public ChargeBeeWebhookCustomer Customer { get; set; }
    public ChargeBeeWebhookSubscription Subscription { get; set; }
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
        default:
          break;
      }

      return Ok();
    }

    public async Task HandleSubscriptionStateChange(ChargeBeeWebhookPayload payload) {
      var regex = new Regex(@"^(user|org)-(\d+)$");
      var matches = regex.Match(payload.Content.Customer.Id);
      var accountId = long.Parse(matches.Groups[2].ToString());

      using (var context = new ShipHubContext()) {
        var sub = await context.Subscriptions
          .Include(x => x.Account)
          .SingleOrDefaultAsync(x => x.AccountId == accountId);

        if (sub == null) {
          // We only care to update subscriptions we've already sync'ed.  This case often happens
          // in development - e.g., you might be testing subscriptions on your local machine, and
          // chargebee delivers webhook calls to shiphub-dev about subscriptions it doesn't know
          // about yet.
          return;
        }

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

        var recordsUpdated = await context.SaveChangesAsync();

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
    }
  }
}