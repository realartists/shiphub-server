namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;

  public class Test {
    public async Task Echo([ServiceBusTrigger("test-echo")] string message, TextWriter logger) {
      await logger.WriteLineAsync(message);
    }

    [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes", Justification = "It's for testing logging. Who cares?")]
    public void Exception([ServiceBusTrigger("test-exception")] string message, TextWriter logger) {
      logger.WriteLine($"[Test Exception]: {message}");
      throw new Exception(message);
    }
  }
}
