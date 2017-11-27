namespace RealArtists.ShipHub.QueueClient {
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel.Types;
  using Messages;

  public interface IShipHubQueueClient {
    Task NotifyChanges(IChangeSummary changeSummary, bool urgent = false);
  }

  public static class ShipHubQueueClientExtensions {
    public static Task Submit(this IChangeSummary summary, IShipHubQueueClient client, bool urgent = false) {
      if (summary == null || summary.IsEmpty) {
        return Task.CompletedTask;
      }

      return client.NotifyChanges(summary, urgent);
    }
  }

  public class ShipHubQueueClient : IShipHubQueueClient {
    IServiceBusFactory _factory;

    public ShipHubQueueClient(IServiceBusFactory serviceBusFactory) {
      _factory = serviceBusFactory;
    }

    public Task NotifyChanges(IChangeSummary changeSummary, bool urgent) {
      return SendIt(ShipHubTopicNames.Changes, new ChangeMessage(changeSummary));
    }

    private async Task SendIt<T>(string queueName, T message) {
      Log.Debug(() => $"{queueName} - {message}");
      var sender = await _factory.MessageSenderForName(queueName);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        await sender.SendAsync(bm);
      }
    }
  }
}
