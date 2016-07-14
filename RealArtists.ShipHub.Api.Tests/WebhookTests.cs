using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Hosting;
using System.Web.Http.Routing;
using AutoMapper;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RealArtists.ShipHub.Api.Controllers;
using RealArtists.ShipHub.Common.DataModel.Types;
using RealArtists.ShipHub.Common.GitHub;
using RealArtists.ShipHub.Common.GitHub.Models;
using RealArtists.ShipHub.QueueClient;
using Xunit;

namespace RealArtists.ShipHub.Api.Tests {
  public class WebhookTests {

    private static string SignatureForPayload(string key, string payload) {
      var hmac = new HMACSHA1(System.Text.Encoding.UTF8.GetBytes(key));
      byte[] hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
      return "sha1=" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
    }
    
    private static IMapper AutoMapper() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<Common.DataModel.GitHubToDataModelProfile>();
      });
      var mapper = config.CreateMapper();
      return mapper;
    }

    private static async Task<IChangeSummary> CallHook(JObject obj) {
      var json = JsonConvert.SerializeObject(obj, GitHubClient.JsonSettings);
      var signature = SignatureForPayload("698DACE9-6267-4391-9B1C-C6F74DB43710", json);
      var webhookGuid = Guid.NewGuid();

      var config = new HttpConfiguration();
      var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/webhook/" + webhookGuid.ToString());
      request.Headers.Add("User-Agent", GitHubWebhookController.GitHubUserAgent);
      request.Headers.Add(GitHubWebhookController.EventHeaderName, "issues");
      request.Headers.Add(GitHubWebhookController.SignatureHeaderName, signature);
      request.Headers.Add(GitHubWebhookController.DeliveryIdHeaderName, Guid.NewGuid().ToString());
      request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));

      var route = config.Routes.MapHttpRoute("blah", "webhook/{rid}");
      var routeData = new HttpRouteData(route, new HttpRouteValueDictionary { { "controller", "test" } });

      IChangeSummary changeSummary = null;

      var mockBusClient = new Mock<IShipHubBusClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask)
        .Callback((IChangeSummary arg) => { changeSummary = arg; });

      var controller = new GitHubWebhookController(mockBusClient.Object);
      controller.ControllerContext = new HttpControllerContext(config, routeData, request);
      controller.Request = request;
      controller.Request.Properties[HttpPropertyKeys.HttpConfigurationKey] = config;

      await controller.HandleHook(webhookGuid);

      return changeSummary;
    }

    [Fact]
    [AutoRollback]
    public async Task TestIssueOpened() {
      var context = new Common.DataModel.ShipHubContext();
      
      var account = new Account() {
        Id = 3001,
        Login = "aroon",
        Type = GitHubAccountType.User,
      };

      var user = (Common.DataModel.User)context.Accounts.Add(new Common.DataModel.User() {
        Id = account.Id,
        Date = DateTimeOffset.Now,
      });
      AutoMapper().Map(account, user);

      var repo = new Common.DataModel.Repository() {
        Id = 2001,
        Name = "myrepo",
        FullName = "aroon/myrepo",
        AccountId = account.Id,
        Private = true,
        Account = user,
        Date = DateTimeOffset.Now,
      };
      context.Repositories.Add(repo);

      await context.SaveChangesAsync();

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        State = "open",
        Number = 1,
        Labels = new List<Label> {
          new Label() {
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
            Color = "0000ff",
            Name = "Blue",
          },
        },
        User = account,
      };

      IChangeSummary changeSummary = await CallHook(new JObject(
        new JProperty("action", "opened"),
        new JProperty("issue", JObject.FromObject(issue, JsonSerializer.CreateDefault(GitHubClient.JsonSettings))),
        new JProperty("sender", null),
        new JProperty("repository", new JObject(
          new JProperty("id", repo.Id)
          )),
        new JProperty("organization", null)
        ));

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { 2001 }, changeSummary.Repositories.ToArray());
     
      var newIssue = context.Issues.FirstOrDefault();
      Assert.Equal(1001, newIssue.Id);
      Assert.Equal(1, newIssue.Number);
      Assert.Equal("Some Title", newIssue.Title);
      Assert.Equal("Some Body", newIssue.Body);
      Assert.Equal("open", newIssue.State);
      Assert.Equal(2001, newIssue.RepositoryId);
      Assert.Equal(3001, newIssue.UserId);

      var labels = newIssue.Labels.OrderBy(x => x.Name).ToArray();
      Assert.Equal(2, labels.Count());
      Assert.Equal("Blue", labels[0].Name);
      Assert.Equal("0000ff", labels[0].Color);
      Assert.Equal("Red", labels[1].Name);
      Assert.Equal("ff0000", labels[1].Color);
    }
  }
}
