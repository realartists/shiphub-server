namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;

  public static class DictionaryHacks {
    /// <summary>
    /// Get the value for the key or a sensible default (added).
    /// </summary>
    /// <returns>The value for the key or a sensible default (added).</returns>
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Vald")]
    public static TValue Vald<TKey, TValue>(this IDictionary<TKey, TValue> self, TKey key, Func<TValue> fallback = null) {
      if (!self.TryGetValue(key, out var result)) {
        result = fallback == null ? default(TValue) : fallback();
        self.Add(key, result);
      }
      return result;
    }

    /// <summary>
    /// Get the value for the key or a new instance (added).
    /// </summary>
    /// <returns>The value for the key or a new instance (added).</returns>
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Valn")]
    public static TValue Valn<TKey, TValue>(this IDictionary<TKey, TValue> self, TKey key)
      where TValue : class, new() {
      if (!self.TryGetValue(key, out var result)) {
        result = new TValue();
        self.Add(key, result);
      }
      return result;
    }

    /// <summary>
    /// Get the value for the key or a the type default (NOT added).
    /// </summary>
    /// <returns>The value for the key or a the type default (NOT added).</returns>
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Val")]
    public static TValue Val<TKey, TValue>(this IDictionary<TKey, TValue> self, TKey key, Func<TValue> fallback = null) {
      if (!self.TryGetValue(key, out var result)) {
        result = fallback == null ? default(TValue) : fallback();
      }
      return result;
    }
  }
}
