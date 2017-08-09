namespace RealArtists.ShipHub.Api {
  using System;
  using System.Linq;
  using System.Reactive.Concurrency;
  using System.Reactive.Disposables;
  using System.Reactive.Linq;
  using Common.DataModel.Types;
  using Microsoft.ServiceBus.Messaging;
  using QueueClient;
  using QueueClient.Messages;

  public interface ISyncManager {
    IObservable<ChangeSummary> Changes { get; }
  }

  public class SyncManager : ISyncManager {
    private const int _BatchSize = 1024;
    private static readonly TimeSpan _WindowTimeout = TimeSpan.FromSeconds(2);

    public IObservable<ChangeSummary> Changes { get; private set; }

    public SyncManager(IServiceBusFactory serviceBusFactory) {
      var messages = Observable
        .Create<ChangeSummary>(async observer => {
          var client = await serviceBusFactory.SubscriptionClientForName(ShipHubTopicNames.Changes);
          client.PrefetchCount = _BatchSize;

          // TODO: convert to batches?
          client.OnMessage(message => {
            var changes = WebJobInterop.UnpackMessage<ChangeMessage>(message);
            observer.OnNext(new ChangeSummary(changes));
          }, new OnMessageOptions() {
            AutoComplete = true,
            AutoRenewTimeout = TimeSpan.FromMinutes(1), // Has to be less than 5 or subscription will idle and expire
            // Should be at least be the number of partitions
            MaxConcurrentCalls = 16
          });

          // When disconnected, stop listening for changes.
          return Disposable.Create(() => client.Close());
        })
        .SubscribeOn(TaskPoolScheduler.Default)
        .Publish()
        .RefCount();

      var urgent = messages.Where(x => x.IsUrgent);
      var coalesced = messages
        .Where(x => !x.IsUrgent)
        .Buffer(_WindowTimeout)
        .Where(x => x.Count > 0)
        .Select(x => ChangeSummary.UnionAll(x));

      Changes = Observable
        .Merge(urgent, coalesced)
        .SubscribeOn(TaskPoolScheduler.Default)
        .Publish()
        .RefCount();
    }
  }
}
