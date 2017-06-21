namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;

  public static class KeyEqualityComparer {
    private static readonly ConcurrentDictionary<Type, object> _ComparerCache = new ConcurrentDictionary<Type, object>();

    public static IEqualityComparer<T> FromKeySelector<T, TKey>(Func<T, TKey> selector) {
      if (_ComparerCache.TryGetValue(typeof(TKey), out var comparer)) {
        return (IEqualityComparer<T>)comparer;
      } else {
        var delegateComparer = EqualityComparer<TKey>.Default;
        var newComparer = new KeyEqualityComparer<T>(
          (T x, T y) => delegateComparer.Equals(selector(x), selector(y)),
          (T obj) => delegateComparer.GetHashCode(selector(obj))
        );
        _ComparerCache.TryAdd(typeof(TKey), newComparer);
        return newComparer;
      }
    }
  }

  public class KeyEqualityComparer<T> : EqualityComparer<T> {
    private Func<T, T, bool> _equals;
    private Func<T, int> _hash;

    public KeyEqualityComparer(Func<T, T, bool> equals, Func<T, int> hash) {
      _equals = equals;
      _hash = hash;
    }

    public override bool Equals(T x, T y) {
      return _equals(x, y);
    }

    public override int GetHashCode(T obj) {
      return _hash(obj);
    }
  }
}
