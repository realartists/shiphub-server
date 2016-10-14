namespace RealArtists.ShipHub.ActorInterfaces.Models {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;

  public class SyncStatus {
    public string Name { get; set; }
    public bool Estimate { get; set; }
    public int Completed { get; set; }
    public int Total { get; set; }
  }
}
