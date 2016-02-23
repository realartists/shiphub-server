namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Collections.Generic;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;

  public class ShipHubContext : DbContext {
    static ShipHubContext() {
      Database.SetInitializer<ShipHubContext>(null);
    }

    public virtual DbSet<object> name { get; set; }

    public ShipHubContext() 
      : this("name=ShipHubContext") {
    }

    public ShipHubContext(string nameOrConnectionString) 
      : base(nameOrConnectionString) {
    }

    public ShipHubContext(DbConnection existingConnection, bool contextOwnsConnection) 
      : base(existingConnection, contextOwnsConnection) {
    }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder modelBuilder) {
    }
  }
}
