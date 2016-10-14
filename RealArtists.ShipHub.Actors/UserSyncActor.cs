namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.DataModel.Types;
  using ActorInterfaces;

  public class UserSyncActor : Orleans.Grain<UserSyncActorState>, IUserSyncActor {
    private long _userId;

    public UserSyncActor(long userId) {
      _userId = userId;
    }

    //public async Task Sync() {
      //using (var context = new ShipHubContext()) {
      //  var tasks = new List<Task>();
      //  ChangeSummary changes = null;

      //  var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.UserId);
      //  if (user == null || user.Token.IsNullOrWhiteSpace()) {
      //    return;
      //  }

      //  logger.WriteLine($"User details for {user.Login} cached until {user.Metadata?.Expires:o}");
      //  if (user.Metadata == null || user.Metadata.Expires < DateTimeOffset.UtcNow) {
      //    logger.WriteLine($"Polling: User");
      //    var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId);
      //    var userResponse = await ghc.User(user.Metadata.IfValidFor(user));

      //    if (userResponse.Status != HttpStatusCode.NotModified) {
      //      logger.WriteLine("GitHub: Changed. Saving changes.");
      //      changes = await context.UpdateAccount(
      //        userResponse.Date,
      //        _mapper.Map<AccountTableType>(userResponse.Result));
      //    } else {
      //      logger.WriteLine($"GitHub: Not modified.");
      //    }

      //    tasks.Add(context.UpdateMetadata("Accounts", user.Id, userResponse));
      //    tasks.Add(notifyChanges.Send(changes));
      //  } else {
      //    logger.WriteLine($"Waiting: Using cache from {user.Metadata.LastRefresh:o}");
      //  }

      //  await Task.WhenAll(tasks);

      //  // Now that the user is saved in the DB, safe to sync all repos and user's orgs
      //  var am = new UserIdMessage(user.Id);
      //  tasks.Add(syncAccountRepos.AddAsync(am));
      //  tasks.Add(syncAccountOrgs.AddAsync(am));

      //  await Task.WhenAll(tasks);
      //}
    //}
  }

  public class UserSyncActorState {
  }
}
