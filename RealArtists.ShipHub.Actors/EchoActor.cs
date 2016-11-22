namespace RealArtists.ShipHub.Actors {
  using System.Threading.Tasks;
  using ActorInterfaces;
  using Orleans;

  public class EchoActor : Grain, IEchoActor {
    public Task<string> Echo(string value) {
      this.Info($"Echo: {value}");
      return Task.FromResult(value);
    }
  }
}
