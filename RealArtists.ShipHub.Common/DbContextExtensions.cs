namespace RealArtists.ShipHub.Common {
  using System;
  using System.Data;
  using System.Data.Entity;
  using System.Threading.Tasks;

  public static class DbContextExtensions {
    public static async Task WithinTransaction(this DbContext context, IsolationLevel isolationLevel, Func<Task> action) {
      var trans = context.Database.CurrentTransaction;
      if (trans != null) {
        var currentLevel = trans.UnderlyingTransaction.IsolationLevel;
        if (trans.UnderlyingTransaction.IsolationLevel == isolationLevel) {
          // Already covered.
          await action();
        } else {
          throw new InvalidOperationException($"Cannot nest transaction isolation level {isolationLevel} within {currentLevel}");
        }
      } else {
        using (var t = context.Database.BeginTransaction(isolationLevel)) {
          try {
            await action();
            t.Commit();
          } catch {
            try { t.Rollback(); } catch { }
            throw;
          }
        }
      }
    }

    public static async Task<T> WithinTransaction<T>(this DbContext context, IsolationLevel isolationLevel, Func<Task<T>> action) {
      var trans = context.Database.CurrentTransaction;
      if (trans != null) {
        var currentLevel = trans.UnderlyingTransaction.IsolationLevel;
        if (trans.UnderlyingTransaction.IsolationLevel == isolationLevel) {
          // Already covered.
          return await action();
        } else {
          throw new InvalidOperationException($"Cannot nest transaction isolation level {isolationLevel} within {currentLevel}");
        }
      } else {
        using (var t = context.Database.BeginTransaction(isolationLevel)) {
          try {
            var result = await action();
            t.Commit();
            return result;
          } catch {
            try { t.Rollback(); } catch { }
            throw;
          }
        }
      }
    }
  }
}

