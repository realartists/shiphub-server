namespace RealArtists.ShipHub.QueueClient {
  using System.IO;
  using System.Text;
  using Microsoft.ServiceBus.Messaging;
  using Newtonsoft.Json;

  // The following has been copied from https://github.com/Azure/azure-webjobs-sdk
  public static class WebJobInterop {
    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\Constants.cs
    public static JsonSerializerSettings JsonSerializerSettings { get; private set; } = new JsonSerializerSettings {
      // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
      // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
      DateParseHandling = DateParseHandling.DateTimeOffset,
      NullValueHandling = NullValueHandling.Ignore,
      Formatting = Formatting.Indented
    };

    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\StrictEncodings.cs
    public static UTF8Encoding Utf8 { get; private set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\Bindings\UserTypeToBrokeredMessageConverter.cs
    public static BrokeredMessage CreateMessage<TInput>(TInput input, string messageId = null, string partitionKey = null) {
      string text = JsonConvert.SerializeObject(input, JsonSerializerSettings);
      byte[] bytes = Utf8.GetBytes(text);
      MemoryStream stream = new MemoryStream(bytes, writable: false);

      var result = new BrokeredMessage(stream, ownsStream: true) {
        ContentType = "application/json",
      };

      if (messageId != null) {
        result.MessageId = messageId;
      }

      if (partitionKey != null) {
        result.PartitionKey = partitionKey;
      }

      return result;
    }
  }
}
