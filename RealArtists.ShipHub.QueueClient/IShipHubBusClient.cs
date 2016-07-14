using System.Threading.Tasks;
using RealArtists.ShipHub.Common.DataModel.Types;

namespace RealArtists.ShipHub.QueueClient {
  public interface IShipHubBusClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task SyncAccount(string accessToken);
  }
}