namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;

  [AllowAnonymous]
  public class GitHubWebhookController : ShipHubController {
    public const string GitHubUserAgent = "GitHub-Hookshot";
    public const string EventHeaderName = "X-GitHub-Event";
    public const string DeliveryIdHeaderName = "X-GitHub-Delivery";
    public const string SignatureHeaderName = "X-Hub-Signature";

    private IShipHubQueueClient _queueClient;

    public GitHubWebhookController(IShipHubQueueClient queueClient) {
      _queueClient = queueClient;
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("webhook/{type:regex(^(org|repo)$)}/{id:long}")]
    public async Task<IHttpActionResult> QueueHook(string type, long id) {
      if (Request.Headers.UserAgent.Single().Product.Name != GitHubUserAgent) {
        return BadRequest("Not you.");
      }

      if (Request.Content.Headers.ContentLength > (64 * 1024)) {
        Log.Error($"Webhook request too large ({Request.Content.Headers.ContentLength} bytes) for {type}:{id}");
        return BadRequest("Request is too large.");
      }

      var eventName = Request.Headers.GetValues(EventHeaderName).Single();
      var deliveryIdHeader = Request.Headers.GetValues(DeliveryIdHeaderName).Single();
      var deliveryId = Guid.Parse(deliveryIdHeader);

      var body = await Request.Content.ReadAsStringAsync();

      var signatureHeader = Request.Headers.GetValues(SignatureHeaderName).Single();
      var signature = SoapHexBinary.Parse(signatureHeader.Substring(5)).Value; // header of form "sha1=..."

      var message = new GitHubWebhookEventMessage() {
        EntityType = type,
        EntityId = id,
        EventName = eventName,
        DeliveryId = deliveryId,
        Payload = body,
        Signature = signature,
      };
      await _queueClient.QueueWebhookEvent(message);

      return Ok();
    }
  }
}
