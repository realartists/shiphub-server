namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;

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

    public static KeyEqualityComparer<T> FromKeySelector<TKey>(Func<T, TKey> selector) {
      var delegateComparer = EqualityComparer<TKey>.Default;
      return new KeyEqualityComparer<T>(
        (T x, T y) => delegateComparer.Equals(selector(x), selector(y)),
        (T obj) => delegateComparer.GetHashCode(selector(obj))
      );
    }
  }
}
