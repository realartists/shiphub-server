namespace RealArtists.ShipHub.Api.Utilities {
  using System;
  using System.Security.Cryptography;
  using DataModel;

  public static class ShipHubAuthenticationUtility {
    public static ShipAuthenticationTokenModel CreateAuthenticationToken(this ShipHubContext context, ShipUserModel user, string clientName) {
      var token = context.AuthenticationTokens.Add(context.AuthenticationTokens.Create());
      token.Id = new Guid(GetRandomBytes(16));
      token.ClientName = clientName;
      token.User = user;
      token.CreationDate = token.LastAccessDate = DateTimeOffset.UtcNow;

      user.AuthenticationTokens.Add(token);
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
