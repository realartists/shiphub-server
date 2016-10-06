﻿namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using ChargeBee.Models;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Jobs;
  using Microsoft.Azure.WebJobs;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;

  public class BillingQueueHandler : LoggingHandlerBase {

    public BillingQueueHandler(IDetailedExceptionLogger logger) : base(logger) { }

    public async Task GetOrCreatePersonalSubscriptionHelper(
      UserIdMessage message,
      IAsyncCollector<ChangeMessage> notifyChanges,
      IGitHubClient gitHubClient,
      TextWriter logger) {
      using (var context = new ShipHubContext()) {
        var user = await context.Users.SingleAsync(x => x.Id == message.UserId);

        var allowedBillingUsers = new[] {
          "fpotter",
          "kogir",
          "james-howard",
          "fpotter-test",
          "aroon", // used in tests
        };
        if (!allowedBillingUsers.Contains(user.Login)) {
          return;
        }

        var customerId = $"user-{message.UserId}";
        var customerList = Customer.List().Id().Is(customerId).Request().List;
        Customer customer = null;
        if (customerList.Count == 0) {
          // Cannot use cache because we need fields like Name + Email which
          // we don't currently save to the DB.
          var githubUser = (await gitHubClient.User(GitHubCacheDetails.Empty)).Result;
          var emails = (await gitHubClient.UserEmails(GitHubCacheDetails.Empty)).Result;
          var primaryEmail = emails.First(x => x.Primary);

          var createRequest = Customer.Create()
            .Id(customerId)
            .Param("cf_github_username", githubUser.Login)
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
          customer = createRequest.Request().Customer;
        } else {
          logger.WriteLine("Billing: Customer already exists");
          customer = customerList.First().Customer;
        }

        var subList = ChargeBee.Models.Subscription.List()
          .CustomerId().Is(customerId)
          .PlanId().Is("personal")
          .Limit(1)
          .SortByCreatedAt(ChargeBee.Filters.Enums.SortOrderEnum.Desc)
          .Request().List;
        ChargeBee.Models.Subscription sub = null;

        if (subList.Count == 0) {
          logger.WriteLine("Billing: Creating personal subscription");
          sub = ChargeBee.Models.Subscription.CreateForCustomer(customerId)
            .PlanId("personal")
            .Request().Subscription;
        } else {
          logger.WriteLine("Billing: Subscription already exists");
          sub = subList.First().Subscription;
        }

        using (var transaction = context.Database.BeginTransaction()) {
          var accountSubscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == user.Id);

          if (accountSubscription == null) {
            accountSubscription = context.Subscriptions.Add(new Common.DataModel.Subscription() {
              AccountId = user.Id,
            });
          }

          switch (sub.Status) {
            case ChargeBee.Models.Subscription.StatusEnum.Active:
            case ChargeBee.Models.Subscription.StatusEnum.NonRenewing:
              accountSubscription.State = SubscriptionState.Subscribed;
              accountSubscription.TrialEndDate = null;
              break;
            case ChargeBee.Models.Subscription.StatusEnum.InTrial:
              accountSubscription.State = SubscriptionState.InTrial;
              accountSubscription.TrialEndDate = new DateTimeOffset(sub.TrialEnd.Value.ToUniversalTime());
              break;
            default:
              accountSubscription.State = SubscriptionState.NotSubscribed;
              accountSubscription.TrialEndDate = null;
              break;
          }

          accountSubscription.Version = sub.GetValue<long>("resource_version");

          int recordsSaved = await context.SaveChangesAsync();

          transaction.Commit();

          if (recordsSaved > 0) {
            var changes = new ChangeSummary();
            changes.Users.Add(user.Id);
            await notifyChanges.AddAsync(new ChangeMessage(changes));
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
        IGitHubClient ghc;
        using (var context = new ShipHubContext()) {
          var user = await context.Users.Where(x => x.Id == message.UserId).SingleOrDefaultAsync();
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }
          ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId);
        }

        await GetOrCreatePersonalSubscriptionHelper(message, notifyChanges, ghc, logger);
      });
    }

    public async Task SyncOrgSubscriptionStateHelper(
      TargetMessage message,
      IAsyncCollector<ChangeMessage> notifyChanges,
      IGitHubClient gitHubClient,
      TextWriter logger) {

      using (var context = new ShipHubContext()) {
        var org = await context.Organizations.SingleAsync(x => x.Id == message.TargetId);

        var customerId = $"org-{message.TargetId}";
        var sub = ChargeBee.Models.Subscription.List()
          .CustomerId().Is(customerId)
          .PlanId().Is("organization")
          .Status().Is(ChargeBee.Models.Subscription.StatusEnum.Active)
          .Limit(1)
          .Request().List.SingleOrDefault()?.Subscription;

        using (var transaction = context.Database.BeginTransaction()) {
          var accountSubscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == message.TargetId);

          if (accountSubscription == null) {
            accountSubscription = context.Subscriptions.Add(new Common.DataModel.Subscription() {
              AccountId = message.TargetId,
            });
          }

          if (sub != null) {
            switch (sub.Status) {
              case ChargeBee.Models.Subscription.StatusEnum.Active:
              case ChargeBee.Models.Subscription.StatusEnum.NonRenewing:
              case ChargeBee.Models.Subscription.StatusEnum.Future:
                accountSubscription.State = SubscriptionState.Subscribed;
                break;
              default:
                accountSubscription.State = SubscriptionState.NotSubscribed;
                break;
            }
            accountSubscription.Version = sub.GetValue<long>("resource_version");
          } else {
            accountSubscription.State = SubscriptionState.NotSubscribed;
          }

          int recordsSaved = await context.SaveChangesAsync();

          transaction.Commit();

          if (recordsSaved > 0) {
            var changes = new ChangeSummary();
            changes.Organizations.Add(message.TargetId);
            await notifyChanges.AddAsync(new ChangeMessage(changes));
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
        IGitHubClient ghc;
        using (var context = new ShipHubContext()) {
          var user = await context.Users.Where(x => x.Id == message.ForUserId).SingleOrDefaultAsync();
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }
          ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId);
        }

        await SyncOrgSubscriptionStateHelper(message, notifyChanges, ghc, logger);
      });
    }

    [Singleton("{UserId}")]
    public async Task UpdateComplimentarySubscription(
      [ServiceBusTrigger(ShipHubQueueNames.BillingUpdateComplimentarySubscription)] UserIdMessage message,
      TextWriter logger,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var couponId = "member_of_paid_org";

          var isMemberOfPaidOrg = await context.OrganizationAccounts
            .CountAsync(x =>
              x.UserId == message.UserId &&
              x.Organization.Subscription.StateName == SubscriptionState.Subscribed.ToString()) > 0;

          var sub = ChargeBee.Models.Subscription.List()
            .CustomerId().Is($"user-{message.UserId}")
            .PlanId().Is("personal")
            .Limit(1)
            .Request().List.First()?.Subscription;

          var hasCoupon = false;

          if (sub.Coupons != null) {
            hasCoupon = sub.Coupons.SingleOrDefault(x => x.CouponId() == couponId) != null;
          }

          if (isMemberOfPaidOrg && !hasCoupon) {
            ChargeBee.Models.Subscription.Update(sub.Id)
              .Coupon(couponId)
              .Request();
          } else if (!isMemberOfPaidOrg && hasCoupon) {
            ChargeBee.Models.Subscription.RemoveCoupons(sub.Id)
              .CouponIds(new List<string>() { couponId })
              .Request();
          }
        }
      });
    }
  }
}
