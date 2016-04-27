namespace RealArtists.ShipHub.Legacy {
  using System.Data.SqlClient;

  public class SqlConnectionFactory {
    private string _connString;

    public SqlConnectionFactory(string connString) {
      _connString = connString;
    }

    public SqlConnection Get() {
      return new SqlConnection(_connString);
    }
  }
}
