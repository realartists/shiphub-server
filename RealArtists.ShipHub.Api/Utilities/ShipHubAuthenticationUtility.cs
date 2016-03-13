namespace RealArtists.ShipHub.Api.Utilities {
  using System;
  using System.Security.Cryptography;
  using DataModel;

  public static class ShipHubAuthenticationUtility {
    public static AuthenticationToken CreateAuthenticationToken(this ShipHubContext context, Account account, string clientName) {
      var token = context.AuthenticationTokens.Add(new AuthenticationToken() {
        Token = new Guid(GetRandomBytes(16)),
        ClientName = clientName,
        Account = account,
        CreationDate = DateTimeOffset.UtcNow,
        LastAccessDate = DateTimeOffset.UtcNow
      });

      account.AuthenticationTokens.Add(token);
      return token;
    }

    public static byte[] GetRandomBytes(int length) {
      var result = new byte[length];
      using (var rng = new RNGCryptoServiceProvider()) {
        rng.GetBytes(result);
      }
      return result;
    }
  }
}
