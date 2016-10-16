namespace RealArtists.ShipHub.Common {
  using System;

  public interface IFactory<T> {
    T CreateInstance();
  }

  public class GenericFactory<T> : IFactory<T> {
    private Func<T> _factoryMethod;

    public GenericFactory(Func<T> factoryMethod) {
      _factoryMethod = factoryMethod;
    }

    public T CreateInstance() {
      return _factoryMethod();
    }
  }
}
