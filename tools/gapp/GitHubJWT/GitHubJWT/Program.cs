namespace GitHubJWT {
  using System;
  using System.IdentityModel.Tokens.Jwt;
  using System.IO;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Claims;
  using System.Security.Cryptography;
  using Microsoft.IdentityModel.Tokens;

  public static class Program {

	// Extract these using openssl rsa -text -noout -in github_private_key.pem
	// Then truncate the output to lengths in powers of two if needed.
  
    public const string modulus = @"
    ";

    public static string publicExponent = "01:00:01";

    public const string privateExponent = @"
    ";

    public const string prime1 = @"
    ";

    public const string prime2 = @"
    ";

    public const string exponent1 = @"
    ";

    public const string exponent2 = @"
    ";

    public const string coefficient = @"
    ";

    static void Main(string[] args) {
      var cspFileName = "real-artists-ship-companion.2017-12-28.private-key.csp";
      var rsa = new RSACryptoServiceProvider();

      if (File.Exists(cspFileName)) {
        var csp64 = File.ReadAllText(cspFileName);
        var csp = Convert.FromBase64String(csp64);
        rsa.ImportCspBlob(csp);
      } else {
        // https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsaparameters?view=netframework-4.7.1
        var rsaParams = new RSAParameters() {
          D = privateExponent.HexBytes(),
          DP = exponent1.HexBytes(),
          DQ = exponent2.HexBytes(),
          Exponent = publicExponent.HexBytes(),
          InverseQ = coefficient.HexBytes(),
          Modulus = modulus.HexBytes(),
          P = prime1.HexBytes(),
          Q = prime2.HexBytes(),
        };
        rsa.ImportParameters(rsaParams);
        var csp = rsa.ExportCspBlob(true);
        var csp64 = Convert.ToBase64String(csp);
        File.WriteAllText(cspFileName, csp64);
      }

      var key = new RsaSecurityKey(rsa);
      var creds = new SigningCredentials(key, "RS256");
      var jwt = new JwtSecurityTokenHandler() {
        SetDefaultTimesOnTokenCreation = true,
        TokenLifetimeInMinutes = 10,
      };

      var header = new JwtHeader(creds);
      var payload = new JwtPayload("7751", null, new[] { new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer) }, null, DateTime.UtcNow.AddMinutes(10));

      var token = new JwtSecurityToken(header, payload);

      var tokenString = jwt.WriteToken(token);
      Console.WriteLine(tokenString);

      Console.ReadKey();
    }

    static byte[] HexBytes(this string input) {
      if (input == null) { return null; }

      var clean = input
        .Replace("\r", "")
        .Replace("\n", "")
        .Replace("\t", "")
        .Replace(" ", "")
        .Replace(":", "");

      var bytes = SoapHexBinary.Parse(clean).Value;
      return bytes;
    }
  }
}
