namespace RealArtists.ShipHub.Api.Models {
  public enum ApiAccountType {
    Unspecified = 0,
    Organization,
    User
  }

  public abstract class ApiAccount {
    public long Identifier { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public ApiAccountType Type { get; set; }
  }
}