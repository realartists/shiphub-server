namespace RealArtists.ShipHub.Api.Models {
  public enum ApiAccountType {
    Unspecified = 0,
    Organization,
    User
  }

  public abstract class ApiAccount {
    public int Identifier { get; set; }
    public string AvatarUrl { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public long? RowVersion { get; set; }
    public ApiAccountType Type { get; set; }
  }
}