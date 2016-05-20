namespace RealArtists.ShipHub.Common {
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Serialization;

  public static class JsonUtility {
    public static readonly JsonSerializerSettings SaneDefaults = CreateSaneDefaultSettings();
    public static readonly JsonSerializer SaneSerializer = JsonSerializer.Create(SaneDefaults);

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
      if (formatting != null) {
        return JsonConvert.SerializeObject(value, formatting.Value, SaneDefaults);
      } else {
        return JsonConvert.SerializeObject(value, SaneDefaults);
      }
    }

    public static T DeserializeObject<T>(this string json) {
      return JsonConvert.DeserializeObject<T>(json, SaneDefaults);
    }

    // Please don't need this.
    //public static T JsonRoundTrip<T>(this object self) {
    //  return JsonUtility.DeserializeObject<T>(JsonHelpers.SerializeObject(self));
    //}
  }
}
