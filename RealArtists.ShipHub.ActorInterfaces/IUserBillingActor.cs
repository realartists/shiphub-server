namespace RealArtists.ShipHub.ActorInterfaces {
  using System;
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.CodeGeneration;

  [Version(1)]
  public interface IUserBillingActor : IGrainWithIntegerKey {
    Task GetOrCreatePersonalSubscription(DateTimeOffset utcNow);
    Task UpdateComplimentarySubscription();
  }
}
