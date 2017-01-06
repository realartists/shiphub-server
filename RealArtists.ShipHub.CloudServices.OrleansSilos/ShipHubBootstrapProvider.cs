namespace RealArtists.ShipHub.CloudServices.OrleansSilos {
  using System;
  using System.Collections.Concurrent;
  using System.Threading.Tasks;
  using Orleans.Providers;
  using Orleans.Serialization;

  public class ShipHubBootstrapProvider : IBootstrapProvider {
    public string Name { get; private set; }

    private ConcurrentDictionary<Type, bool> _exceptionSerializability = new ConcurrentDictionary<Type, bool>();

    public Task Close() {
      return Task.CompletedTask;
    }

    public Task Init(string name, IProviderRuntime providerRuntime, IProviderConfiguration config) {
      Name = name;

      // Force exceptions to be serializable
      providerRuntime.SetInvokeInterceptor(async (method, request, grain, invoker) => {
        try {
          return await invoker.Invoke(grain, request);
        } catch (Exception ex) {
          var exType = ex.GetType();
          bool serializable;

          if (!_exceptionSerializability.TryGetValue(exType, out serializable)) {
            serializable = Attribute.IsDefined(exType, typeof(SerializableAttribute));
            _exceptionSerializability.TryAdd(exType, serializable);
          }

          if (serializable) {
            throw;
          } else {
            throw new Exception(ex.ToString());
          }
        }
      });

      return Task.CompletedTask;
    }
  }
}
