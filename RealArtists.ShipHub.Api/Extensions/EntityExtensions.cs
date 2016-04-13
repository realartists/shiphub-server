namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;
  using System.Data.Entity;

  public static class EntityExtensions {
    public static TEntity New<TEntity>(this DbSet<TEntity> dbSet)
      where TEntity : class {
      return dbSet.Add(dbSet.Create());
    }

    public static TDerivedEntity New<TEntity, TDerivedEntity>(this DbSet<TEntity> dbSet)
      where TEntity : class
      where TDerivedEntity : class, TEntity {
      return (TDerivedEntity)dbSet.Add(dbSet.Create<TDerivedEntity>());
    }
  }
}
