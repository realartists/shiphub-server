namespace RealArtists.ShipHub.ActorInterfaces.Serialization {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using Common;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using Orleans.CodeGeneration;
  using Orleans.Serialization;

  [RegisterSerializer]
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

    [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
    static OrleansJTokenSerializer() {
      Register();
    }

    public static object DeepCopier(object original) {
      // Even though JTokens *should* only be read, they can be edited. Let's play it safe.
      return ((JToken)original)?.DeepClone();
    }

    public static void Serializer(object untypedInput, BinaryTokenStreamWriter stream, Type expected) {
      var input = (JToken)untypedInput;
      string json = JsonConvert.SerializeObject(input, _JsonSerializerSettings);
      SerializationManager.SerializeInner(json, stream, typeof(string));
    }

    public static object Deserializer(Type expected, BinaryTokenStreamReader stream) {
      var json = (string)SerializationManager.DeserializeInner(typeof(string), stream);
      return JsonConvert.DeserializeObject(json, expected, _JsonSerializerSettings);
    }

    public static void Register() {
      Log.Trace();

      SerializationManager.Register(typeof(JToken), DeepCopier, Serializer, Deserializer);

      // I guess maybe I have to register all descendant types too o_O

      SerializationManager.Register(typeof(JValue), DeepCopier, Serializer, Deserializer);
      SerializationManager.Register(typeof(JRaw), DeepCopier, Serializer, Deserializer);

      SerializationManager.Register(typeof(JContainer), DeepCopier, Serializer, Deserializer);
      SerializationManager.Register(typeof(JArray), DeepCopier, Serializer, Deserializer);
      SerializationManager.Register(typeof(JConstructor), DeepCopier, Serializer, Deserializer);
      SerializationManager.Register(typeof(JObject), DeepCopier, Serializer, Deserializer);
      SerializationManager.Register(typeof(JProperty), DeepCopier, Serializer, Deserializer);
    }
  }
}
