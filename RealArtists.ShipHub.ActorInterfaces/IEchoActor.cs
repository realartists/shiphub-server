namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;

  public interface IEchoActor : Orleans.IGrainWithIntegerKey {
    Task<string> Echo(string value);
  }
}
