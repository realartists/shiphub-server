namespace ConsoleSpider {
  using System;
  using Newtonsoft.Json;
  using RealArtists.Ship.Server.QueueClient;

  class Program {
    const string NickToken = "d2a92e54aa081836dc52eda768d4ab35fc0c225c";

    static void Main(string[] args) {
      ResourceUpdateClient.EnsureQueues().Wait();

      var result = new SpiderSession(NickToken);

      result.Run().Wait();

      Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
      Console.ReadKey();
    }
  }
}
