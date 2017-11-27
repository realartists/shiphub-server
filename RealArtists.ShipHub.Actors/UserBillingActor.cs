namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using QueueClient;
  using cb = ChargeBee;
  using cbm = ChargeBee.Models;
  using cm = Common.DataModel;

  /// <summary>
  /// For now keep this separate as an intermediate step.
  /// In actuality pieces should probably move to UserActor and OrganzationActor respectively.
  /// </summary>
  public class UserBillingActor : Grain, IUserBillingActor {
    // anyone whose trial has expired earlier than the AmnestyDate will get a restart on it.
    public static DateTimeOffset AmnestyDate { get; } = new DateTimeOffset(2017, 7, 10, 0, 0, 0, TimeSpan.Zero);

    // Injected
    private IGrainFactory _grainFactory;
    private IFactory<cm.ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;
    private cb.ChargeBeeApi _chargeBee;

    // Grain state
    private long _userId;
    private IGitHubActor _github;

    public UserBillingActor(IGrainFactory grainFactory, IFactory<cm.ShipHubContext> contextFactory, IShipHubQueueClient queueClient, cb.ChargeBeeApi chargeBee) {
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
      _chargeBee = chargeBee;
    }

    public override async Task OnActivateAsync() {
      // Set this first as subsequent calls require it.
      _userId = this.GetPrimaryKeyLong();

      // Ensure this user actually exists, and lookup their token.
      cm.User user = null;
      using (var context = _contextFactory.CreateInstance()) {
        user = await context.Users
         .AsNoTracking()
         .Include(x => x.Tokens)
         .SingleOrDefaultAsync(x => x.Id == _userId);
      }

      if (user == null) {
        throw new InvalidOperationException($"User {_userId} does not exist and cannot be activated.");
      }

      if (!user.Tokens.Any()) {
        throw new InvalidOperationException($"User {_userId} has an invalid token and cannot be activated.");
      }

      _github = _grainFactory.GetGrain<IGitHubActor>(_userId);

      await base.OnActivateAsync();
    }

    public async Task GetOrCreatePersonalSubscription(DateTimeOffset utcNow) {
      var customerId = $"user-{_userId}";
      var customerList = (await _chargeBee.Customer.List().Id().Is(customerId).Request()).List;
      cbm.Customer customer = null;
      if (customerList.Count == 0) {
        // Cannot use cache because we need fields like Name + Email which
        // we don't currently save to the DB.
        var githubUser = (await _github.User()).Result;
        var emails = (await _github.UserEmails()).Result;
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

        this.Debug(() => "Billing: Creating customer");
        customer = (await createRequest.Request()).Customer;
      } else {
        this.Debug(() => "Billing: Customer already exists");
        customer = customerList.First().Customer;
      }

      var subList = (await _chargeBee.Subscription.List()
        .CustomerId().Is(customerId)
        .PlanId().In(ChargeBeeUtilities.PersonalPlanIds)
        .Limit(1)
        .SortByCreatedAt(cb.Filters.Enums.SortOrderEnum.Desc)
        .Request()).List;
      cbm.Subscription sub = null;

      var trialEnd = utcNow.AddDays(14).ToUnixTimeSeconds();
      if (subList.Count == 0) {
        var metaData = new ChargeBeePersonalSubscriptionMetadata() {
          // If someone purchases a personal subscription while their free trial is still
          // going, we'll need to know the trial peroid length so we can give the right amount
          // of credit for unused time.
          TrialPeriodDays = 14,
        };

        this.Debug(() => "Billing: Creating personal subscription");
        sub = (await _chargeBee.Subscription.CreateForCustomer(customerId)
          .PlanId("personal")
          .TrialEnd(trialEnd)
          .MetaData(JObject.FromObject(metaData, GitHubSerialization.JsonSerializer))
          .Request()).Subscription;
      } else {
        this.Debug(() => "Billing: Subscription already exists");
        sub = subList.First().Subscription;
      }

      if (sub.Status == cbm.Subscription.StatusEnum.Cancelled && (sub.CancelledAt ?? DateTime.UtcNow) < AmnestyDate) {
        this.Debug(() => "Billing: Applying subscription amnesty for cancelled subscription");
        // delete any payment sources that we've got for this person
        if (customer.PaymentMethod != null) {
          this.Debug(() => "Billing: Deleting existing card for cancelled subscription");
          await _chargeBee.Card.DeleteCardForCustomer(customerId).Request();
        }
        sub = (await _chargeBee.Subscription.Reactivate(sub.Id).TrialEnd(trialEnd).Request()).Subscription;
        // realartists/shiphub-server#539 Don't turn off billing auto-collection when reinstating trials
        // Re-enable auto-collection, otherwise at the end of the trial period an invoice will be generated.
        await _chargeBee.Customer.Update(customerId).AutoCollection(cbm.Enums.AutoCollectionEnum.On).Request();
      }

      ChangeSummary changes;
      using (var context = new cm.ShipHubContext()) {
        var accountSubscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == _userId);

        if (accountSubscription == null) {
          accountSubscription = new cm.Subscription() { AccountId = _userId, };
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

      await changes.Submit(_queueClient, urgent: true);

      await UpdateComplimentarySubscription();
    }

    public async Task UpdateComplimentarySubscription() {
      var couponId = "member_of_paid_org";

      var sub = (await _chargeBee.Subscription.List()
        .CustomerId().Is($"user-{_userId}")
        .PlanId().In(ChargeBeeUtilities.PersonalPlanIds)
        .Limit(1)
        .Request()).List.FirstOrDefault()?.Subscription;

      var shouldHaveCoupon = false;
      using (var context = new cm.ShipHubContext()) {
        var isMemberOfPaidOrg = await context.OrganizationAccounts
          .AsNoTracking()
          .Where(x =>
            x.UserId == _userId &&
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
    }
  }
}
