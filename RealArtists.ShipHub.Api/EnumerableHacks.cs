namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;
  using System.Linq;


  public static class EnumerableHacks {
    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source) {
      return new HashSet<T>(source);
    }

    /// <summary>
    /// Returns elements in source after and and including the first element matching condition.
    /// </summary>
    /// <typeparam name="T">The type of the elements of source.</typeparam>
    /// <param name="source">A sequence of values.</param>
    /// <param name="condition">A condition to determine the first element returned.</param>
    /// <returns>All elements in source after and and including the first element matching condition.</returns>
    public static IEnumerable<T> BeginWith<T>(this IEnumerable<T> source, Func<T, bool> condition) {
      bool triggered = false;

      foreach (var elem in source) {
        if (triggered) {
          yield return elem;
        } else if (condition(elem)) {
          triggered = true;
          yield return elem;
        }
      }

      if (!triggered) {
        throw new IndexOutOfRangeException("No element satisfied the condition.");
      }
    }

    /// <summary>
    /// Returns elements in source after and and excluding the first element matching condition.
    /// </summary>
    /// <typeparam name="T">The type of the elements of source.</typeparam>
    /// <param name="source">A sequence of values.</param>
    /// <param name="condition">A condition to determine the first element returned.</param>
    /// <returns>All elements in source after and and excluding the first element matching condition.</returns>
    public static IEnumerable<T> After<T>(this IEnumerable<T> source, Func<T, bool> condition) {
      bool triggered = false;

      foreach (var elem in source) {
        if (triggered) {
          yield return elem;
        } else if (condition(elem)) {
          triggered = true;
        }
      }

      if (!triggered) {
        throw new IndexOutOfRangeException("No element satisfied the condition.");
      }
    }

    /// <summary>
    /// Returns elements in source before and excluding the first element matching condition.
    /// </summary>
    /// <typeparam name="T">The type of the elements of source.</typeparam>
    /// <param name="source">A sequence of values.</param>
    /// <param name="condition">A condition to determine the final element returned.</param>
    /// <returns>All elements in source before and excluding the first element matching condition.</returns>
    public static IEnumerable<T> Until<T>(this IEnumerable<T> source, Func<T, bool> condition) {
      foreach (var elem in source) {
        if (condition(elem)) {
          break;
        } else {
          yield return elem;
        }
      }
    }

    /// <summary>
    /// Returns all elements of a sequence up to and including the first matching the condition.
    /// </summary>
    /// <typeparam name="T">The type of the elements of source.</typeparam>
    /// <param name="source">A sequence of values.</param>
    /// <param name="condition">A condition to determine the final element returned.</param>
    /// <returns>All elements in source up to and including the first element matching condition.</returns>
    public static IEnumerable<T> EndWith<T>(this IEnumerable<T> source, Func<T, bool> condition) {
      bool triggered = false;

      foreach (var elem in source) {
        if (condition(elem)) {
          triggered = true;
          yield return elem;
          break;
        } else {
          yield return elem;
        }
      }

      if (!triggered) {
        throw new IndexOutOfRangeException("No element satisfied the condition.");
      }
    }

    /// <summary>
    /// Invokes a transform function on each element of a generic sequence and returns
    //  the maximum resulting value, or the provided default if the sequence is empty.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of source.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by selector.</typeparam>
    /// <param name="source">A sequence of values to determine the maximum value of.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <param name="fallback">The default value to return if the source sequence is empty.</param>
    /// <returns>The maximum value in the sequence or the provided default if the sequence is empty.</returns>
    public static TResult Max<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector, TResult fallback) {
      if (source.Any()) {
        return source.Max(selector);
      } else {
        return fallback;
      }
    }
  }
}