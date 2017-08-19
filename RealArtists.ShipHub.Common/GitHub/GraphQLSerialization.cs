namespace RealArtists.ShipHub.Common.GitHub {
  using System.Collections.Generic;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Serialization;

  public static class GraphQLSerialization {
    public static MediaTypeHeaderValue JsonMediaType { get; } = new MediaTypeHeaderValue("application/json");

    public static JsonSerializerSettings JsonSerializerSettings { get; } = CreateSerializerSettings();
    public static JsonSerializer JsonSerializer { get; } = JsonSerializer.Create(JsonSerializerSettings);

    public static JsonMediaTypeFormatter JsonMediaTypeFormatter { get; } = new JsonMediaTypeFormatter() { SerializerSettings = JsonSerializerSettings };
    public static IEnumerable<MediaTypeFormatter> MediaTypeFormatters { get; } = new[] { JsonMediaTypeFormatter };

    private static JsonSerializerSettings CreateSerializerSettings() {
      var settings = new JsonSerializerSettings() {
        ContractResolver = new DefaultContractResolver() {
          NamingStrategy = new CamelCaseNamingStrategy(),
        },
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
      };

      settings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = false,
        CamelCaseText = false,
      });

      return settings;
    }
  }
}
