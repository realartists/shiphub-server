using System.Data.Entity;
using System.Data.Entity.SqlServer;

namespace RealArtists.ShipHub.Common.DataModel {
  /// <summary>
  /// Use SqlAzureExecutionStrategy?
  /// http://ritzlgrmft.blogspot.com/2014/03/working-with-sqlazureexecutionstrategy.html
  /// </summary>
  public class ShipHubContextConfiguration : DbConfiguration {
    public ShipHubContextConfiguration() {
      SetExecutionStrategy("System.Data.SqlClient", () => new SqlAzureExecutionStrategy());
    }
  }
}
