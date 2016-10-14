namespace RealArtists.ShipHub.Actors {
  using System.Threading.Tasks;
  using ActorInterfaces;
  using Orleans;

  public class EchoActor : Grain, IEchoActor {
    public Task<string> Echo(string value) {
      return Task.FromResult(value);
    }

    public Task<long> EchoKey() {
      return Task.FromResult(this.GetPrimaryKeyLong());
    }
  }
}
