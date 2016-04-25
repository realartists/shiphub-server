namespace RealArtists.ShipHub.Api.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;

  public class WebhookConfiguration {
    public string Url { get; set; }
    public string ContentType { get; set; }
    public string Secret { get; set; }
    public int InsecureSsl { get; set; }
  }

  public class Webhook {
    public string Name { get; set; }
    public WebhookConfiguration Config { get; set; }
    public IEnumerable<EventType> Events { get; set; }
    public bool Active { get; set; }
  }
}