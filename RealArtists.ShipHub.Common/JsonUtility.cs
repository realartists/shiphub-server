namespace RealArtists.ShipHub.Common {
  using System.Collections.Generic;
  using System.IO;
  using System.Net.Http.Formatting;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Serialization;

  public static class JsonUtility {
    public static JsonSerializerSettings JsonSerializerSettings { get; } = CreateSaneDefaultSettings();
    public static JsonSerializer JsonSerializer { get; } = JsonSerializer.Create(JsonSerializerSettings);

    public static JsonMediaTypeFormatter JsonMediaTypeFormatter { get; } = new JsonMediaTypeFormatter() { SerializerSettings = JsonSerializerSettings };
    public static IEnumerable<MediaTypeFormatter> MediaTypeFormatters { get; } = new[] { JsonMediaTypeFormatter };

    public static JsonSerializerSettings CreateSaneDefaultSettings() {
      var settings = new JsonSerializerSettings() {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
      };

      settings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = true, // Needed to serialize unofficial HTTP status codes GitHub uses.
      });

      return settings;
    }

    public static string SerializeObject(this object value, Formatting? formatting = null, JsonSerializerSettings serializerSettings = null) {
      if (value == null) {
        return null;
      }

      formatting = formatting ?? Formatting.None;
      serializerSettings = serializerSettings ?? JsonSerializerSettings;

      return JsonConvert.SerializeObject(value, formatting.Value, serializerSettings);

    }

    public static T DeserializeObject<T>(this string json, JsonSerializerSettings serializerSettings = null)
      where T : class {
      if (json.IsNullOrWhiteSpace()) {
        return null;
      }

      serializerSettings = serializerSettings ?? JsonSerializerSettings;

      return JsonConvert.DeserializeObject<T>(json, serializerSettings);
    }

    public static T Roundtrip<T>(this object value, JsonSerializerSettings serializerSettings = null, JsonSerializerSettings deserializerSettings = null)
      where T : class {
      serializerSettings = serializerSettings ?? JsonSerializerSettings;
      deserializerSettings = deserializerSettings ?? serializerSettings;
      return value?.SerializeObject(serializerSettings: serializerSettings).DeserializeObject<T>(deserializerSettings);
    }

    public static string DumpJson(this object value, string logMessage) {
#if DEBUG
      var tempDir = Path.GetTempPath();
      var tempFile = Path.GetRandomFileName() + ".json";
      var tempInfo = new FileInfo(Path.Combine(tempDir, tempFile));

      using (var sw = tempInfo.CreateText()) {
        JsonSerializer.Serialize(sw, value);
      }

      Log.Info($"{logMessage} Dumpfile: {tempInfo.FullName}");

      return tempInfo.FullName;
#else
      return null;
#endif
    }
  }
}
