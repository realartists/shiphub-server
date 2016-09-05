namespace RealArtists.ShipHub.QueueProcessor {
  using Microsoft.Azure.WebJobs.ServiceBus;
  using Microsoft.ServiceBus;
  using Microsoft.ServiceBus.Messaging;
  using QueueClient;

  public class PairedMessagingProvider : MessagingProvider {
    IServiceBusFactory _factory;

    public PairedMessagingProvider(ServiceBusConfiguration config, IServiceBusFactory serviceBusFactory) : base(config) {
      _factory = serviceBusFactory;
    }

    // Use defaults for these
    // This extension interface is gross and makes me sad.

    //public override MessageProcessor CreateMessageProcessor(string entityPath) {
    //  return base.CreateMessageProcessor(entityPath);
    //}

    //public override MessageReceiver CreateMessageReceiver(MessagingFactory factory, string entityPath) {
    //  return base.CreateMessageReceiver(factory, entityPath);
    //}

    public override MessagingFactory CreateMessagingFactory(string entityPath, string connectionStringName = null) {
      return _factory.MessagingFactory;
    }

    public override NamespaceManager CreateNamespaceManager(string connectionStringName = null) {
      return _factory.NamespaceManager;
    }
  }
}
