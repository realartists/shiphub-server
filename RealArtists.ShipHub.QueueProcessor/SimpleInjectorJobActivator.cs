namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using Microsoft.Azure.WebJobs.Host;

  public class SimpleInjectorJobActivator : IJobActivator {
    private IServiceProvider _serviceProvider;

    public SimpleInjectorJobActivator(IServiceProvider serviceProvider) {
      _serviceProvider = serviceProvider;
    }

    public T CreateInstance<T>() {
      return (T)_serviceProvider.GetService(typeof(T));
    }
  }
}
