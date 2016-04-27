namespace GitHubUpdateProcessor {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using System.IO;
  using Microsoft.Azure.WebJobs;
  using RealArtists.Ship.Server.QueueClient.GitHubUpdate;
  using g = RealArtists.ShipHub.Common.GitHub.Models;
  using RealArtists.ShipHub.Common.DataModel;
  using System.Data.Entity;
  using AutoMapper;

  public static class GitHubUpdates {
    // TODO: Locking?
    // TODO: Notifications

    public static IMapper Mapper { get; private set; }

    public static void Register() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
        //cfg.AddProfile<DataModelToApiModelProfile>();
      });
      Mapper = config.CreateMapper();
    }

    public static async Task UpdateAccount(
      [ServiceBusTrigger(GitHubQueueNames.Account)] UpdateMessage<g.Account> message,
      TextWriter log) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        var account = await context.Accounts.SingleOrDefaultAsync(x => x.Id == update.Id);
        if (account == null) {
          if (update.Type == g.GitHubAccountType.User) {
            account = context.Accounts.Add(new User());
          } else {
            account = context.Accounts.Add(new Organization());
          }
          account.Id = update.Id;
        }
        Mapper.Map(update, account);

        await context.SaveChangesAsync();
      }
    }
  }
}
