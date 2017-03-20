namespace RealArtists.ShipHub.ActorInterfaces.Serialization {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using Common;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using Orleans.CodeGeneration;
  using Orleans.Serialization;

  [Serializer(typeof(JToken))]
  // I guess maybe I have to register all descendant types too o_O
  [Serializer(typeof(JValue))]
  [Serializer(typeof(JRaw))]
  [Serializer(typeof(JContainer))]
  [Serializer(typeof(JArray))]
  [Serializer(typeof(JConstructor))]
  [Serializer(typeof(JObject))]
  [Serializer(typeof(JProperty))]
  internal class OrleansJTokenSerializer {
    // Can't make the class static or Orleans won't find it.
    private OrleansJTokenSerializer() { }

    private static readonly JsonSerializerSettings _JsonSerializerSettings = new JsonSerializerSettings {
      // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
      // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
      DateParseHandling = DateParseHandling.DateTimeOffset,
      NullValueHandling = NullValueHandling.Ignore,
      Formatting = Formatting.Indented
    };

    [CopierMethod]
    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "context")]
    [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
    public static object DeepCopier(object original, ICopyContext context) {
      // Even though JTokens *should* only be read, they can be edited. Let's play it safe.
      return ((JToken)original)?.DeepClone();
    }

    [SerializerMethod]
    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "expected")]
    [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
    public static void Serializer(object untypedInput, ISerializationContext context, Type expected) {
      var input = (JToken)untypedInput;
      var json = JsonConvert.SerializeObject(input, _JsonSerializerSettings);
      SerializationManager.SerializeInner(json, context, typeof(string));
    }

    [DeserializerMethod]
    [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
    public static object Deserializer(Type expected, IDeserializationContext context) {
      var json = (string)SerializationManager.DeserializeInner(typeof(string), context);
      return JsonConvert.DeserializeObject(json, expected, _JsonSerializerSettings);
    }
  }
}
