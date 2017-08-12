namespace RealArtists.ShipHub.Common {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Bson;
  using Newtonsoft.Json.Linq;
  using Orleans.Runtime;
  using Orleans.Serialization;

  public class JsonObjectSerializer : IExternalSerializer {
    private static readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings {
      // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
      // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
      DateParseHandling = DateParseHandling.DateTimeOffset,
      NullValueHandling = NullValueHandling.Ignore,
      Formatting = Formatting.None,
    };

    private static readonly JsonSerializer _jsonSerializer = JsonSerializer.Create(_jsonSerializerSettings);

    public JsonObjectSerializer() {
      // Parameterless default constructor required.
    }

    public void Initialize(Logger logger) {
    }

    public bool IsSupportedType(Type itemType) {
      return itemType.IsSubclassOf(typeof(JToken));
    }

    public object DeepCopy(object source, ICopyContext context) {
      // Even though JTokens *should* only be read, they can be edited. Let's play it safe.
      return ((JToken)source)?.DeepClone();
    }

    [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public object Deserialize(Type expectedType, IDeserializationContext context) {
      var bytes = context.SerializationManager.Deserialize<byte[]>(context.StreamReader);
      using (var ms = new MemoryStream(bytes, false))
      using (var bsonReader = new BsonDataReader(ms, false, DateTimeKind.Utc)) {
        return _jsonSerializer.Deserialize(bsonReader, expectedType);
      }
    }

    [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public void Serialize(object item, ISerializationContext context, Type expectedType) {
      var input = (JToken)item;
      using (var ms = new MemoryStream()) {
        using (var bsonWriter = new BsonDataWriter(ms)) {
          _jsonSerializer.Serialize(bsonWriter, input, expectedType);
        }
        context.SerializationManager.Serialize(ms.ToArray(), context.StreamWriter);
      }
    }
  }
}
