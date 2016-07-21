namespace RealArtists.ShipHub.QueueClient {
  using RealArtists.ShipHub.Common.DataModel.Types;
  using System.Threading.Tasks;

  public interface IShipHubBusClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task SyncAccount(string accessToken);
  }
}