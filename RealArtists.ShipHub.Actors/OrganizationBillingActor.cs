namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Orleans;
  using QueueClient;
  using cb = ChargeBee;
  using cbm = ChargeBee.Models;

  public class OrganizationBillingActor : Grain, IOrganizationBillingActor {
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;
    private cb.ChargeBeeApi _chargeBee;

    private long _orgId;

    public OrganizationBillingActor(
      IFactory<ShipHubContext> contextFactory,
      IShipHubQueueClient queueClient,
      cb.ChargeBeeApi chargeBee) {
      _contextFactory = contextFactory;
      _queueClient = queueClient;
      _chargeBee = chargeBee;
    }

    public override async Task OnActivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        var orgId = this.GetPrimaryKeyLong();

        // Ensure this organization actually exists
        var org = await context.Organizations
          .SingleOrDefaultAsync(x => x.Id == orgId);

        if (org == null) {
          throw new InvalidOperationException($"Organization {orgId} does not exist and cannot be activated.");
        }

        _orgId = orgId;
      }

      await base.OnActivateAsync();
    }

    public async Task SyncSubscriptionState() {
      Subscription orgSub;
      using (var context = _contextFactory.CreateInstance()) {
        // Lookup orgs
        orgSub = await context.Organizations
          .AsNoTracking()
          .Where(x => x.Id == _orgId)
          .Select(x => x.Subscription)
          .SingleOrDefaultAsync();
      }
      var customerId = $"org-{_orgId}";

      // Get subscription from ChargeBee
      cbm.Subscription cbSub;
      var response = await _chargeBee.Subscription.List()
        .CustomerId().Is(customerId)
        .PlanId().Is("organization")
        .Status().Is(cbm.Subscription.StatusEnum.Active)
        .Request();

      cbSub = response.List.Select(x => x.Subscription).SingleOrDefault();

      // Process any updates
      if (orgSub == null) {
        orgSub = new Subscription() { AccountId = _orgId };
      }

      if (cbSub != null) {
        switch (cbSub.Status) {
          case cbm.Subscription.StatusEnum.Active:
          case cbm.Subscription.StatusEnum.NonRenewing:
          case cbm.Subscription.StatusEnum.Future:
            orgSub.State = SubscriptionState.Subscribed;
            break;
          default:
            orgSub.State = SubscriptionState.NotSubscribed;
            break;
        }
        orgSub.Version = cbSub.GetValue<long>("resource_version");
      } else {
        orgSub.State = SubscriptionState.NotSubscribed;
      }

      ChangeSummary changes;
      using (var context = _contextFactory.CreateInstance()) {
        changes = await context.BulkUpdateSubscriptions(
          new[] {
            new SubscriptionTableType() {
              AccountId = orgSub.AccountId,
              State = orgSub.StateName,
              TrialEndDate = orgSub.TrialEndDate,
              Version = orgSub.Version,
            }
          }
        );
      }

      await changes.Submit(_queueClient, urgent: true);
    }
  }
}
