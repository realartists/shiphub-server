namespace RealArtists.ShipHub.ActorInterfaces {
  public interface IChangeObserver : Orleans.IGrainObserver {
    void Changed();
  }
}
