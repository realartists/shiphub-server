namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;
  using Tracing;

  public class TestQueueHandler : LoggingHandlerBase {
    public TestQueueHandler(IDetailedExceptionLogger logger) : base(logger) { }

    public async Task Echo([ServiceBusTrigger("test-echo")] string message, TextWriter logger) {
      await logger.WriteLineAsync(message);
    }

    [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes", Justification = "It's for testing logging. Who cares?")]
    public async Task Exception([ServiceBusTrigger("test-exception")] string message, TextWriter logger, ExecutionContext context) {
      await WithEnhancedLogging(context.InvocationId, null, message, async () => {
        await logger.WriteLineAsync($"[Test Exception]: {message}");
        throw new Exception(message);
      });
    }
  }
}
