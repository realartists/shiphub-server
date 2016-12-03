using System.Data.Entity;
using System.Data.Entity.SqlServer;

namespace RealArtists.ShipHub.Common.DataModel {
  /// <summary>
  /// Use SqlAzureExecutionStrategy.
  /// http://ritzlgrmft.blogspot.com/2014/03/working-with-sqlazureexecutionstrategy.html
  /// </summary>
  public class ShipHubContextConfiguration : DbConfiguration {
    private static readonly ShipHubCloudConfiguration _Config = new ShipHubCloudConfiguration();
    public ShipHubContextConfiguration() {
      if (_Config.UseSqlAzureExecutionStrategy) {
        SetExecutionStrategy("System.Data.SqlClient", () => new SqlAzureExecutionStrategy());
      }
    }
  }
}
