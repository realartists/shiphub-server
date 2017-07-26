namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans.CodeGeneration;

  [Version(1)]
  public interface IEchoActor : Orleans.IGrainWithIntegerKey {
    Task<string> Echo(string value);
  }
}
