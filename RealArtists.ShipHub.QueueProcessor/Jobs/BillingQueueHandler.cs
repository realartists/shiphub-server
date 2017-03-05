namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Data.Entity.Infrastructure;
  using System.IO;
  using System.Linq;
  using System.Runtime.Serialization;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;
  using cb = ChargeBee;
  using cbm = ChargeBee.Models;
  using cm = Common.DataModel;
  using gh = Common.GitHub;

  public class BillingQueueHandler : LoggingHandlerBase {
    private IGrainFactory _grainFactory;
    private cb.ChargeBeeApi _chargeBee;

    public BillingQueueHandler(IGrainFactory grainFactory, IDetailedExceptionLogger logger, cb.ChargeBeeApi chargeBee)
      : base(logger) {
      _grainFactory = grainFactory;
      _chargeBee = chargeBee;
    }

    public async Task GetOrCreatePersonalSubscriptionHelper(
      UserIdMessage message,
      IAsyncCollector<ChangeMessage> notifyChanges,
      IGitHubActor gitHubClient,
      TextWriter logger,
      DateTimeOffset? utcNow = null) {

      var customerId = $"user-{message.UserId}";
      var customerList = (await _chargeBee.Customer.List().Id().Is(customerId).Request()).List;
      cbm.Customer customer = null;
      if (customerList.Count == 0) {
        // Cannot use cache because we need fields like Name + Email which
        // we don't currently save to the DB.
        var githubUser = (await gitHubClient.User()).Result;
        var emails = (await gitHubClient.UserEmails()).Result;
        var primaryEmail = emails.First(x => x.Primary);

        var createRequest = _chargeBee.Customer.Create()
          .Id(customerId)
          .AddParam("cf_github_username", githubUser.Login)
          .Email(primaryEmail.Email);

        // Name is optional for GitHub.
        if (!githubUser.Name.IsNullOrWhiteSpace()) {
          var nameParts = githubUser.Name.Trim().Split(' ');
          var firstName = string.Join(" ", nameParts.Take(nameParts.Count() - 1));
          var lastName = nameParts.Last();
          createRequest.FirstName(firstName);
          createRequest.LastName(lastName);
        }

        logger.WriteLine("Billing: Creating customer");
        customer = (await createRequest.Request()).Customer;
      } else {
        logger.WriteLine("Billing: Customer already exists");
        customer = customerList.First().Customer;
      }

      var subList = (await _chargeBee.Subscription.List()
        .CustomerId().Is(customerId)
        .PlanId().Is("personal")
        .Limit(1)
        .SortByCreatedAt(cb.Filters.Enums.SortOrderEnum.Desc)
        .Request()).List;
      cbm.Subscription sub = null;

      if (subList.Count == 0) {
        var trialEnd = (utcNow ?? DateTimeOffset.UtcNow).AddDays(14).ToUnixTimeSeconds();
        var metaData = new ChargeBeePersonalSubscriptionMetadata() {
          // If someone purchases a personal subscription while their free trial is still
          // going, we'll need to know the trial peroid length so we can give the right amount
          // of credit for unused time.
          TrialPeriodDays = 14,
        };

        logger.WriteLine("Billing: Creating personal subscription");
        sub = (await _chargeBee.Subscription.CreateForCustomer(customerId)
          .PlanId("personal")
          .TrialEnd(trialEnd)
          .MetaData(JObject.FromObject(metaData, GitHubSerialization.JsonSerializer))
          .Request()).Subscription;
      } else {
        logger.WriteLine("Billing: Subscription already exists");
        sub = subList.First().Subscription;
      }

      ChangeSummary changes;
      using (var context = new cm.ShipHubContext()) {
        var accountSubscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == message.UserId);

        if (accountSubscription == null) {
          accountSubscription = new cm.Subscription() { AccountId = message.UserId, };
        }

        var version = sub.ResourceVersion.GetValueOrDefault(0);
        if (accountSubscription.Version > version) {
          // Drop old data
          return;
        } else {
          accountSubscription.Version = version;
        }

        switch (sub.Status) {
          case cbm.Subscription.StatusEnum.Active:
          case cbm.Subscription.StatusEnum.NonRenewing:
            accountSubscription.State = cm.SubscriptionState.Subscribed;
            accountSubscription.TrialEndDate = null;
            break;
          case cbm.Subscription.StatusEnum.InTrial:
            accountSubscription.State = cm.SubscriptionState.InTrial;
            accountSubscription.TrialEndDate = new DateTimeOffset(sub.TrialEnd.Value.ToUniversalTime());
            break;
          default:
            accountSubscription.State = cm.SubscriptionState.NotSubscribed;
            accountSubscription.TrialEndDate = null;
            break;
        }

        changes = await context.BulkUpdateSubscriptions(new[] {
          new SubscriptionTableType(){
            AccountId = accountSubscription.AccountId,
            State = accountSubscription.StateName,
            TrialEndDate = accountSubscription.TrialEndDate,
            Version = accountSubscription.Version,
          },
        });
      }

      if (!changes.IsEmpty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    [Singleton("{UserId}")]
    public async Task GetOrCreatePersonalSubscription(
      [ServiceBusTrigger(ShipHubQueueNames.BillingGetOrCreatePersonalSubscription)] UserIdMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        var gh = _grainFactory.GetGrain<IGitHubActor>(message.UserId);
        await GetOrCreatePersonalSubscriptionHelper(message, notifyChanges, gh, logger);
      });
    }

    public async Task SyncOrgSubscriptionStateHelper(
      SyncOrgSubscriptionStateMessage message,
      IAsyncCollector<ChangeMessage> notifyChanges,
      IGitHubActor gitHubClient,
      TextWriter logger) {

      IEnumerable<cm.Organization> orgs;
      using (var context = new cm.ShipHubContext()) {
        // Lookup orgs
        orgs = await context.Organizations
          .AsNoTracking()
          .Where(x => message.OrgIds.Contains(x.Id))
          .Include(x => x.Subscription)
          .ToArrayAsync();
      }
      var orgCustomerIds = orgs.Select(x => $"org-{x.Id}").ToArray();

      // Get subscriptions from ChargeBee
      var subsById = new Dictionary<long, cbm.Subscription>();
      string offset = null;
      do {
        var response = await _chargeBee.Subscription.List()
          .CustomerId().In(orgCustomerIds)
          .PlanId().Is("organization")
          .Status().Is(cbm.Subscription.StatusEnum.Active)
          .Offset(offset)
          .SortByCreatedAt(cb.Filters.Enums.SortOrderEnum.Asc)
          .Request();

        foreach (var sub in response.List.Select(x => x.Subscription)) {
          subsById.Add(ChargeBeeUtilities.AccountIdFromCustomerId(sub.CustomerId), sub);
        }

        offset = response.NextOffset;
      } while (!offset.IsNullOrWhiteSpace());

      // Process any updates
      foreach (var org in orgs) {
        if (org.Subscription == null) {
          org.Subscription = new cm.Subscription() { AccountId = org.Id };
        }

        var sub = subsById.ContainsKey(org.Id) ? subsById[org.Id] : null;

        if (sub != null) {
          switch (sub.Status) {
            case cbm.Subscription.StatusEnum.Active:
            case cbm.Subscription.StatusEnum.NonRenewing:
            case cbm.Subscription.StatusEnum.Future:
              org.Subscription.State = cm.SubscriptionState.Subscribed;
              break;
            default:
              org.Subscription.State = cm.SubscriptionState.NotSubscribed;
              break;
          }
          org.Subscription.Version = sub.GetValue<long>("resource_version");
        } else {
          org.Subscription.State = cm.SubscriptionState.NotSubscribed;
        }
      }

      ChangeSummary changes;
      using (var context = new cm.ShipHubContext()) {
        changes = await context.BulkUpdateSubscriptions(
          orgs.Select(x =>
          new SubscriptionTableType() {
            AccountId = x.Subscription.AccountId,
            State = x.Subscription.StateName,
            TrialEndDate = x.Subscription.TrialEndDate,
            Version = x.Subscription.Version,
          })
        );
      }

      if (!changes.IsEmpty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    public async Task SyncOrgSubscriptionState(
      [ServiceBusTrigger(ShipHubQueueNames.BillingSyncOrgSubscriptionState)] SyncOrgSubscriptionStateMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger,
      ExecutionContext executionContext) {

      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        var gh = _grainFactory.GetGrain<IGitHubActor>(message.ForUserId);
        await SyncOrgSubscriptionStateHelper(message, notifyChanges, gh, logger);
      });
    }

    [Singleton("{UserId}")]
    public async Task UpdateComplimentarySubscription(
      [ServiceBusTrigger(ShipHubQueueNames.BillingUpdateComplimentarySubscription)] UserIdMessage message,
      TextWriter logger,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        var couponId = "member_of_paid_org";

        var sub = (await _chargeBee.Subscription.List()
          .CustomerId().Is($"user-{message.UserId}")
          .PlanId().Is("personal")
          .Limit(1)
          .Request()).List.FirstOrDefault()?.Subscription;

        bool shouldHaveCoupon = false;
        using (var context = new cm.ShipHubContext()) {
          var isMemberOfPaidOrg = await context.OrganizationAccounts
            .AsNoTracking()
            .Where(x =>
              x.UserId == message.UserId &&
              x.Organization.Subscription.StateName == cm.SubscriptionState.Subscribed.ToString())
            .AnyAsync();

          shouldHaveCoupon = isMemberOfPaidOrg;
        }

        // We should only apply this coupon to already active subscriptions, and never
        // to free trials.  If we apply it to a free trial, the amount due at the end
        // of term would be $0, and the subscription would automatically transition to
        // active even though no payment method is set.
        if (sub != null &&
            sub.Status != cbm.Subscription.StatusEnum.Active &&
            sub.Status != cbm.Subscription.StatusEnum.NonRenewing) {
          shouldHaveCoupon = false;
        }

        if (sub != null) {
          var hasCoupon = false;

          if (sub.Coupons != null) {
            hasCoupon = sub.Coupons.SingleOrDefault(x => x.CouponId() == couponId) != null;
          }

          if (shouldHaveCoupon && !hasCoupon) {
            await _chargeBee.Subscription.Update(sub.Id)
              .CouponIds(new List<string>() { couponId }) // ChargeBee API definitely not written by .NET devs.
              .Request();
          } else if (!shouldHaveCoupon && hasCoupon) {
            await _chargeBee.Subscription.RemoveCoupons(sub.Id)
              .CouponIds(new List<string>() { couponId })
              .Request();
          }
        }
      });
    }
  }
}
