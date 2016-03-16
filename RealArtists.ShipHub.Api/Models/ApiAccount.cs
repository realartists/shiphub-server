namespace RealArtists.ShipHub.Api.Models {
  public enum ApiAccountType {
    Organization,
    User
  }

  public abstract class ApiAccount {
    public string AvatarUrl { get; set; }
    public int Identifier { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
    public long RowVersion { get; set; }
    public ApiAccountType Type { get; set; }
  }
}