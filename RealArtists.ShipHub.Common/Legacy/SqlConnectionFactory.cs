namespace RealArtists.ShipHub.Legacy {
  using System.Data.SqlClient;

  public class SqlConnectionFactory {
    private string _connString;

    public SqlConnectionFactory(string connectionString) {
      _connString = connectionString;
    }

    public SqlConnection Get() {
      return new SqlConnection(_connString);
    }
  }
}
