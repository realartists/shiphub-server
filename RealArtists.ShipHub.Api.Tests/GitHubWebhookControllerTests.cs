namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Controllers;
  using System.Web.Http.Hosting;
  using System.Web.Http.Results;
  using System.Web.Http.Routing;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Controllers;
  using Microsoft.Azure.WebJobs;
  using Moq;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using NUnit.Framework;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using QueueProcessor.Jobs;
  using QueueProcessor.Tracing;

  [TestFixture]
  [AutoRollback]
  public class GitHubWebhookControllerTests {

    private static string SignatureForPayload(string key, string payload) {
      var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
      byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
      return "sha1=" + new SoapHexBinary(hash).ToString();
    }
    
    private static void ConfigureController(ApiController controller, string eventName, JObject body, string secretKey) {
      var json = JsonConvert.SerializeObject(body, GitHubSerialization.JsonSerializerSettings);
      var signature = SignatureForPayload(secretKey, json);

      var config = new HttpConfiguration();
      var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/webhook");
      request.Headers.Add("User-Agent", GitHubWebhookController.GitHubUserAgent);
      request.Headers.Add(GitHubWebhookController.EventHeaderName, eventName);
      request.Headers.Add(GitHubWebhookController.SignatureHeaderName, signature);
      request.Headers.Add(GitHubWebhookController.DeliveryIdHeaderName, Guid.NewGuid().ToString());
      request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
      var routeData = new HttpRouteData(config.Routes.MapHttpRoute("Webhook", "webhook"));

      controller.ControllerContext = new HttpControllerContext(config, routeData, request);
      controller.Request = request;
      controller.Request.Properties[HttpPropertyKeys.HttpConfigurationKey] = config;
    }
    
    [Test]
    public async Task QueueHookWillQueueWebhookEvent() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = 5001,
          },
        }, GitHubSerialization.JsonSerializer);

        GitHubWebhookEventMessage message = null;
        var mockBusClient = new Mock<IShipHubQueueClient>();
        mockBusClient.Setup(x => x.QueueWebhookEvent(It.IsAny<GitHubWebhookEventMessage>()))
          .Returns((GitHubWebhookEventMessage arg) => {
            message = arg;
            return Task.CompletedTask;
          });

        var controller = new GitHubWebhookController(mockBusClient.Object);
        ConfigureController(controller, "ping", obj, "somesecret");
        var result = await controller.QueueHook("repo", 5001);
        Assert.IsInstanceOf(typeof(OkResult), result);

        var expectedJson = JsonConvert.SerializeObject(obj, GitHubSerialization.JsonSerializerSettings);
        var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("somesecret"));
        byte[] expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(expectedJson));
        
        Assert.NotNull(message, "Should have queued a message to process the webhook event");
        Assert.AreEqual(5001, message.EntityId);
        Assert.AreEqual("repo", message.EntityType);
        Assert.AreEqual(expectedSignature, message.Signature);
        Assert.AreEqual("ping", message.EventName);
        Assert.NotNull(message.DeliveryId);
        Assert.AreEqual(expectedJson, message.Payload);
      }
    }
  }
}
