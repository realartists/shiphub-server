namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Data.Entity.Infrastructure;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel.Types;
  using Microsoft.Azure.WebJobs;
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
      TextWriter logger) {
      using (var context = new cm.ShipHubContext()) {
        var user = await context.Users.SingleAsync(x => x.Id == message.UserId);

        var customerId = $"user-{message.UserId}";
        var customerList = (await _chargeBee.Customer.List().Id().Is(customerId).Request()).List;
        cbm.Customer customer = null;
        if (customerList.Count == 0) {
          // Cannot use cache because we need fields like Name + Email which
          // we don't currently save to the DB.
          var githubUser = (await gitHubClient.User(gh.GitHubCacheDetails.Empty)).Result;
          var emails = (await gitHubClient.UserEmails(gh.GitHubCacheDetails.Empty)).Result;
          var primaryEmail = emails.First(x => x.Primary);

          var createRequest = _chargeBee.Customer.Create()
            .Id(customerId)
            .AddParam("cf_github_username", githubUser.Login)
            .Email(primaryEmail.Email);

          // Name is optional for Github.
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
          logger.WriteLine("Billing: Creating personal subscription");
          sub = (await _chargeBee.Subscription.CreateForCustomer(customerId)
            .PlanId("personal")
            .Request()).Subscription;
        } else {
          logger.WriteLine("Billing: Subscription already exists");
          sub = subList.First().Subscription;
        }

        for (int attempt = 0; attempt < 2; ++attempt) {
          try {
            var accountSubscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == user.Id);

            if (accountSubscription == null) {
              accountSubscription = context.Subscriptions.Add(new cm.Subscription() {
                AccountId = user.Id,
              });
            }

            var version = sub.ResourceVersion.GetValueOrDefault(0);
            if (accountSubscription.Version > version) {
              // Drop old data
              break;
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

            int recordsSaved = await context.SaveChangesAsync();

            if (recordsSaved > 0) {
              var changes = new ChangeSummary();
              changes.Users.Add(user.Id);
              await notifyChanges.AddAsync(new ChangeMessage(changes));
            }

            // Success. Don't retry.
            break;
          } catch (DbUpdateConcurrencyException) {
          }
        }
      }
    }

    [Singleton("{UserId}")]
    public async Task GetOrCreatePersonalSubscription(
      [ServiceBusTrigger(ShipHubQueueNames.BillingGetOrCreatePersonalSubscription)] UserIdMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        IGitHubActor gh;
        using (var context = new cm.ShipHubContext()) {
          var user = await context.Users.Where(x => x.Id == message.UserId).SingleOrDefaultAsync();
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }
          gh = _grainFactory.GetGrain<IGitHubActor>(user.Id);
        }

        await GetOrCreatePersonalSubscriptionHelper(message, notifyChanges, gh, logger);
      });
    }

    public async Task SyncOrgSubscriptionStateHelper(
      TargetMessage message,
      IAsyncCollector<ChangeMessage> notifyChanges,
      IGitHubActor gitHubClient,
      TextWriter logger) {

      using (var context = new cm.ShipHubContext()) {
        var org = await context.Organizations.SingleAsync(x => x.Id == message.TargetId);

        var customerId = $"org-{message.TargetId}";
        var sub = (await _chargeBee.Subscription.List()
          .CustomerId().Is(customerId)
          .PlanId().Is("organization")
          .Status().Is(cbm.Subscription.StatusEnum.Active)
          .Limit(1)
          .Request()).List.SingleOrDefault()?.Subscription;

        for (int attempt = 0; attempt < 2; ++attempt) {
          try {
            var accountSubscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == message.TargetId);

            if (accountSubscription == null) {
              accountSubscription = context.Subscriptions.Add(new cm.Subscription() {
                AccountId = message.TargetId,
              });
            }

            if (sub != null) {
              switch (sub.Status) {
                case cbm.Subscription.StatusEnum.Active:
                case cbm.Subscription.StatusEnum.NonRenewing:
                case cbm.Subscription.StatusEnum.Future:
                  accountSubscription.State = cm.SubscriptionState.Subscribed;
                  break;
                default:
                  accountSubscription.State = cm.SubscriptionState.NotSubscribed;
                  break;
              }
              accountSubscription.Version = sub.GetValue<long>("resource_version");
            } else {
              accountSubscription.State = cm.SubscriptionState.NotSubscribed;
            }

            int recordsSaved = await context.SaveChangesAsync();

            if (recordsSaved > 0) {
              var changes = new ChangeSummary();
              changes.Organizations.Add(message.TargetId);
              await notifyChanges.AddAsync(new ChangeMessage(changes));
            }

            // Success. Don't retry.
            break;
          } catch (DbUpdateConcurrencyException) {
          }
        }
      }
    }

    public async Task SyncOrgSubscriptionState(
      [ServiceBusTrigger(ShipHubQueueNames.BillingSyncOrgSubscriptionState)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger,
      ExecutionContext executionContext) {

      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        IGitHubActor gh;
        using (var context = new cm.ShipHubContext()) {
          var user = await context.Users.Where(x => x.Id == message.ForUserId).SingleOrDefaultAsync();
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }
          gh = _grainFactory.GetGrain<IGitHubActor>(user.Id);
        }

        await SyncOrgSubscriptionStateHelper(message, notifyChanges, gh, logger);
      });
    }

    [Singleton("{UserId}")]
    public async Task UpdateComplimentarySubscription(
      [ServiceBusTrigger(ShipHubQueueNames.BillingUpdateComplimentarySubscription)] UserIdMessage message,
      TextWriter logger,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        using (var context = new cm.ShipHubContext()) {
          var couponId = "member_of_paid_org";

          var sub = (await _chargeBee.Subscription.List()
            .CustomerId().Is($"user-{message.UserId}")
            .PlanId().Is("personal")
            .Limit(1)
            .Request()).List.FirstOrDefault()?.Subscription;

          var isMemberOfPaidOrg = await context.OrganizationAccounts
            .CountAsync(x =>
              x.UserId == message.UserId &&
              x.Organization.Subscription.StateName == cm.SubscriptionState.Subscribed.ToString()) > 0;

          bool shouldHaveCoupon = isMemberOfPaidOrg;

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
        }
      });
    }
  }
}
