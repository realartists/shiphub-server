namespace RealArtists.ShipHub.Api.Controllers {
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Text;
  using System.Web.Http;
  using RealArtists.ShipHub.Common;

  [AllowAnonymous]
  [RoutePrefix(".well-known")]
  public class ApplePayController : ApiController {
    private IShipHubConfiguration _config;

    public ApplePayController(IShipHubConfiguration config) {
      _config = config;
    }

    [HttpGet]
    [Route("apple-developer-merchantid-domain-association")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public HttpResponseMessage GetAppleDeveloperMerchantIdDomainAssociation(HttpRequestMessage request) {
      var admda = _config.AppleDeveloperMerchantIdDomainAssociation;
      if (admda.IsNullOrWhiteSpace()) {
        return request.CreateErrorResponse(HttpStatusCode.NotFound, "Not Found");
      } else {
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Content = new StringContent(admda, Encoding.UTF8, MediaTypeNames.Text.Plain);
        return response;
      }
    }
  }
}
