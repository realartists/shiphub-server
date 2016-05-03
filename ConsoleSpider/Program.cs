namespace ConsoleSpider {
  using System;
  using System.Diagnostics;
  using Newtonsoft.Json;
  using RealArtists.Ship.Server.QueueClient;

  class Program {
    const string NickToken = "d2a92e54aa081836dc52eda768d4ab35fc0c225c";

    static void Main(string[] args) {
      ResourceUpdateClient.EnsureQueues().Wait();

      var result = new SpiderSession(NickToken);

      Stopwatch timer = new Stopwatch();
      timer.Restart();
      result.Run().Wait();
      timer.Stop();

      Console.WriteLine($"Done. Elapsed: {timer.Elapsed}");
      //Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
      Console.ReadKey();
    }
  }
}
