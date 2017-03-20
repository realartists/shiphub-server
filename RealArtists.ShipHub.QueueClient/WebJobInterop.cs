namespace RealArtists.ShipHub.QueueClient {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Text;
  using Microsoft.ServiceBus.Messaging;
  using Newtonsoft.Json;

  // The following has been copied from https://github.com/Azure/azure-webjobs-sdk
  public static class WebJobInterop {
    private const string _ContentType = "application/json";

    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\Constants.cs
    private static readonly JsonSerializerSettings _JsonSerializerSettings = new JsonSerializerSettings {
      // The default value, DateParseHandling.DateTime, drops time zone information from DateTimeOffets.
      // This value appears to work well with both DateTimes (without time zone information) and DateTimeOffsets.
      DateParseHandling = DateParseHandling.DateTimeOffset,
      NullValueHandling = NullValueHandling.Ignore,
      Formatting = Formatting.Indented
    };

    private static readonly JsonSerializer _JsonSerializer = JsonSerializer.Create(_JsonSerializerSettings);

    // From azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\StrictEncodings.cs
    private static readonly UTF8Encoding _Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Based on azure-webjobs-sdk\src\Microsoft.Azure.WebJobs.ServiceBus\Bindings\UserTypeToBrokeredMessageConverter.cs
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public static BrokeredMessage CreateMessage<TInput>(TInput input, string messageId = null, string partitionKey = null) {
      var text = JsonConvert.SerializeObject(input, _JsonSerializerSettings);
      var bytes = _Utf8.GetBytes(text);
      var stream = new MemoryStream(bytes, writable: false);

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

    [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public static T UnpackMessage<T>(BrokeredMessage message) {
      if (!message.ContentType.Equals(_ContentType, StringComparison.Ordinal)) {
        throw new InvalidOperationException($"Content type '{message.ContentType}' is not supported. Should be '{_ContentType}'.");
      }

      using (var stream = message.GetBody<Stream>())
      using (var tr = new StreamReader(stream, _Utf8))
      using (var jsr = new JsonTextReader(tr)) {
        return _JsonSerializer.Deserialize<T>(jsr);
      }
    }
  }
}
