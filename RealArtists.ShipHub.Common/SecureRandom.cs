namespace RealArtists.ShipHub.Common {
  using System;
  using System.Security.Cryptography;

  public static class SecureRandom {
    public static Guid GenerateGuid() {
      return new Guid(GenerateBytes(16));
    }

    public static byte[] GenerateBytes(int length) {
      using (var random = RandomNumberGenerator.Create()) {
        var result = new byte[length];
        random.GetBytes(result);
        return result;
      }
    }
  }
}
