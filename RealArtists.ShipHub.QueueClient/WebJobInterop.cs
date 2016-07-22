namespace RealArtists.ShipHub.QueueClient {
  using System;
  using System.IO;
  using System.Text;
  using Microsoft.ServiceBus.Messaging;
  using Newtonsoft.Json;

  // The following has been copied from https://github.com/Azure/azure-webjobs-sdk
  public static class WebJobInterop {
    private const string _ContentType = "application/json";

    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\Constants.cs
    public static JsonSerializerSettings JsonSerializerSettings { get; private set; } = new JsonSerializerSettings {
      // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
      // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
      DateParseHandling = DateParseHandling.DateTimeOffset,
      NullValueHandling = NullValueHandling.Ignore,
      Formatting = Formatting.Indented
    };

    public static JsonSerializer JsonSerializer { get; private set; } = JsonSerializer.Create(JsonSerializerSettings);

    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\StrictEncodings.cs
    public static UTF8Encoding Utf8 { get; private set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Based on azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\Bindings\UserTypeToBrokeredMessageConverter.cs
    public static BrokeredMessage CreateMessage<TInput>(TInput input, string messageId = null, string partitionKey = null) {
      string text = JsonConvert.SerializeObject(input, JsonSerializerSettings);
      byte[] bytes = Utf8.GetBytes(text);
      MemoryStream stream = new MemoryStream(bytes, writable: false);

      var result = new BrokeredMessage(stream, ownsStream: true) {
        ContentType = _ContentType,
      };

      if (messageId != null) {
        result.MessageId = messageId;
      }

      if (partitionKey != null) {
        result.PartitionKey = partitionKey;
      }

      return result;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public static T UnpackMessage<T>(BrokeredMessage message) {
      if (!message.ContentType.Equals(_ContentType, StringComparison.Ordinal)) {
        throw new InvalidOperationException($"Content type '{message.ContentType}' is not supported. Should be '{_ContentType}'.");
      }

      using (var stream = message.GetBody<Stream>())
      using (var tr = new StreamReader(stream, Utf8))
      using (var jsr = new JsonTextReader(tr)) {
        return JsonSerializer.Deserialize<T>(jsr);
      }
    }
  }
}
