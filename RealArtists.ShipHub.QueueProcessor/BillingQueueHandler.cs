namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Data.Entity;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using ChargeBee.Models;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Jobs;
  using Microsoft.Azure.WebJobs;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;

  public class BillingQueueHandler : LoggingHandlerBase {

    public BillingQueueHandler(IDetailedExceptionLogger logger) : base(logger) {
    }

    public async Task GetOrCreateSubscriptionHelper(
      UserIdMessage message,
      IAsyncCollector<ChangeMessage> notifyChanges,
      IGitHubClient ghc,
      TextWriter logger) {
      using (var context = new ShipHubContext()) {
        var user = await context.Users.SingleAsync(x => x.Id == message.UserId);

        var customerId = $"user-{message.UserId}";
        var customerList = Customer.List().Id().Is(customerId).Request().List;
        Customer customer = null;
        if (customerList.Count == 0) {
          // Cannot use cache because we need fields like Name + Email which
          // we don't currently save to the DB.
          var githubUser = (await ghc.User(GitHubCacheDetails.Empty)).Result;

          var nameParts = githubUser.Name.Trim().Split(' ');
          var firstName = string.Join(" ", nameParts.Take(nameParts.Count() - 1));
          var lastName = nameParts.Last();

          customer = Customer.Create()
            .Id(customerId)
            .FirstName(firstName)
            .LastName(lastName)
            .Request().Customer;
        } else {
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
          sub = ChargeBee.Models.Subscription.CreateForCustomer(customerId)
            .PlanId("personal")
            .Request().Subscription;

          sub = ChargeBee.Models.Subscription.Cancel(sub.Id)
            .EndOfTerm(true)
            .Request().Subscription;
        } else {
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
              break;
            case ChargeBee.Models.Subscription.StatusEnum.InTrial:
              accountSubscription.State = SubscriptionState.InTrial;
              accountSubscription.TrialEndDate = new DateTimeOffset(sub.TrialEnd.Value);
              break;
            default:
              accountSubscription.State = SubscriptionState.NoSubscription;
              break;
          }

          await context.SaveChangesAsync();

          transaction.Commit();
        }
      }
    }

    public async Task GetOrCreateSubscription(
      [ServiceBusTrigger(ShipHubQueueNames.BillingGetOrCreateSubscription)] UserIdMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger,
      ExecutionContext executionContext) {

      IGitHubClient ghc;
      using (var context = new ShipHubContext()) {
        var user = await context.Users.Where(x => x.Id == message.UserId).SingleOrDefaultAsync();
        if (user == null || user.Token.IsNullOrWhiteSpace()) {
          return;
        }
        ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId);
      }

      await GetOrCreateSubscriptionHelper(message, notifyChanges, ghc, logger);
    }
  }
}
