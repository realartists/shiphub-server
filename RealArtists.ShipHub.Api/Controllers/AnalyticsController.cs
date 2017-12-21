namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common;
  using Common.GitHub;
  using Newtonsoft.Json;

  public class AnalyticsEvent {
    public string Event { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, string> Properties { get; set; }
  }

  [RoutePrefix("analytics")]
  public class AnalyticsController : ApiController {
    public static Uri MixpanelApi { get; } = new Uri("https://api.mixpanel.com/track/");

    private IShipHubConfiguration _configuration;
    private static HttpClient _Client { get; } = CreateHttpClient();

    public AnalyticsController(IShipHubConfiguration config) {
      _configuration = config;
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static HttpClient CreateHttpClient() {
      HttpUtilities.SetServicePointConnectionLimit(MixpanelApi);

      var httpClient = new HttpClient(HttpUtilities.CreateDefaultHandler(), true);

      var headers = httpClient.DefaultRequestHeaders;
      headers.UserAgent.Clear();
      headers.UserAgent.Add(new ProductInfoHeaderValue("RealArtists", "1.0"));

      return httpClient;
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("track")]
    public async Task<HttpResponseMessage> Track() {
      var payloadEvents = await Request.Content.ReadAsAsync<IEnumerable<AnalyticsEvent>>(GitHubSerialization.MediaTypeFormatters);

      foreach (var payloadEvent in payloadEvents) {
        payloadEvent.Properties["token"] = _configuration.MixpanelToken;
        payloadEvent.Properties["ip"] = Request.Headers.ParseHeader("X-Forwarded-For", x => x);
      }

      var json = JsonConvert.SerializeObject(payloadEvents, GitHubSerialization.JsonSerializerSettings);
      var jsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

      var request = new HttpRequestMessage(HttpMethod.Post, MixpanelApi) {
        Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", jsonBase64) }),
      };

      return await _Client.SendAsync(request);
    }
  }
}
