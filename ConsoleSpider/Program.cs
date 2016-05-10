namespace ConsoleSpider {
  using System;
  using System.Diagnostics;
  using RealArtists.Ship.Server.QueueClient;

  class Program {
    const string NickToken = "d2a92e54aa081836dc52eda768d4ab35fc0c225c";

    static void Main(string[] args) {
      Console.WriteLine("Creating queues if needed.");
      ShipHubQueueClient.EnsureQueues().Wait();

      var result = new SpiderSession(NickToken);

      Console.WriteLine("Begin spider");
      Stopwatch timer = new Stopwatch();
      timer.Restart();
      result.Run().Wait();
      timer.Stop();

      Console.WriteLine($"End spider\n\nElapsed: {timer.Elapsed}");
      //Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
      Console.ReadKey();
    }
  }
}
