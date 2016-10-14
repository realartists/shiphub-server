namespace RealArtists.ShipHub.ActorInterfaces {
  using Models;

  public interface ISyncStatusObserver : Orleans.IGrainObserver {
    void StatusChanged(SyncStatus status);
  }
}
