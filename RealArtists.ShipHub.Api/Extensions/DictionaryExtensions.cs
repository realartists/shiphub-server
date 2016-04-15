namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;

  public static class DictionaryHacks {
    /// <summary>
    /// Get the value for the key or a sensible default (added).
    /// </summary>
    /// <returns>The value for the key or a sensible default (added).</returns>
    public static V Vald<K, V>(this IDictionary<K, V> self, K key, Func<V> fallback = null) {
      V result;
      if (!self.TryGetValue(key, out result)) {
        result = fallback == null ? default(V) : fallback();
        self.Add(key, result);
      }
      return result;
    }

    /// <summary>
    /// Get the value for the key or a new instance (added).
    /// </summary>
    /// <returns>The value for the key or a new instance (added).</returns>
    public static V Valn<K, V>(this IDictionary<K, V> self, K key)
      where V : class, new() {
      V result;
      if (!self.TryGetValue(key, out result)) {
        result = new V();
        self.Add(key, result);
      }
      return result;
    }

    /// <summary>
    /// Get the value for the key or a the type default (NOT added).
    /// </summary>
    /// <returns>The value for the key or a the type default (NOT added).</returns>
    public static V Val<K, V>(this IDictionary<K, V> self, K key, Func<V> fallback = null) {
      V result;
      if (!self.TryGetValue(key, out result)) {
        result = fallback == null ? default(V) : fallback();
      }
      return result;
    }
  }
}
