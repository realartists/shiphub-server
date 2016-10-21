namespace RealArtists.ShipHub.ActorInterfaces.Injection {
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using Orleans;

  public class LazyGrainFactory : IGrainFactory {
    private Lazy<IGrainFactory> _grainFactory;

    public LazyGrainFactory(Func<IGrainFactory> valueFactory) {
      _grainFactory = new Lazy<IGrainFactory>(valueFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<TGrainObserverInterface> CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver {
      return _grainFactory.Value.CreateObjectReference<TGrainObserverInterface>(obj);
    }

    public Task DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver {
      return _grainFactory.Value.DeleteObjectReference<TGrainObserverInterface>(obj);
    }

    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey {
      return _grainFactory.Value.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    }

    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey {
      return _grainFactory.Value.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    }

    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey {
      return _grainFactory.Value.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
    }

    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey {
      return _grainFactory.Value.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    }

    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey {
      return _grainFactory.Value.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
    }
  }
}
