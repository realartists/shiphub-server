namespace RealArtists.ShipHub.Common {
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Serialization;

  public static class JsonUtility {
    public static JsonSerializerSettings SaneDefaults { get; } = CreateSaneDefaultSettings();
    public static JsonSerializer SaneSerializer { get; } = JsonSerializer.Create(SaneDefaults);

    public static JsonSerializerSettings CreateSaneDefaultSettings() {
      var settings = new JsonSerializerSettings() {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
      };

      settings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = false,
      });

      return settings;
    }

    public static string SerializeObject(this object value, Formatting? formatting = null) {
      if (value == null) {
        return null;
      }

      if (formatting != null) {
        return JsonConvert.SerializeObject(value, formatting.Value, SaneDefaults);
      } else {
        return JsonConvert.SerializeObject(value, SaneDefaults);
      }
    }

    public static T DeserializeObject<T>(this string json)
      where T : class {
      if (string.IsNullOrWhiteSpace(json)) {
        return null;
      }

      return JsonConvert.DeserializeObject<T>(json, SaneDefaults);
    }
  }
}
