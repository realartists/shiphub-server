namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System.Diagnostics.CodeAnalysis;

  public class AccountTableType {
    public long Id { get; set; }

    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    public string Type { get; set; }

    public string Login { get; set; }
  }
}
