namespace RealArtists.GitHub.TestConsole {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Tests;

  public class Program {
    public static void Main(string[] args) {
      var a = new CanEven();
      a.GetAUser("kogir").Wait();
      a.GetAUser("james-howard").Wait();
      a.EtagNotModified().Wait();
      a.LastModifiedSince().Wait();

      Console.WriteLine("Done.");
      Console.ReadLine();
    }
  }
}
