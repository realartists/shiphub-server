﻿namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System.Collections.Generic;

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