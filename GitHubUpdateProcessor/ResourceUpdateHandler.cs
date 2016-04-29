namespace GitHubUpdateProcessor {
  using System.Data.Entity;
  using System.IO;
  using System.Threading.Tasks;
  using AutoMapper;
  using Microsoft.Azure.WebJobs;
  using RealArtists.Ship.Server.QueueClient.ResourceUpdate;
  using RealArtists.ShipHub.Common.DataModel;

  public static class ResourceUpdateHandler {
    // TODO: Locking?
    // TODO: Notifications

    /* TODO generally:
     * If updated with token and cache metadata is available, record it.
     * If not, wipe it.
     * When updated via a webhook, update last seen for the hook, and mark the item as refreshed by the hook.
     * Notifications if changed
     */

    public static IMapper Mapper { get; private set; }

    static ResourceUpdateHandler() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
        //cfg.AddProfile<DataModelToApiModelProfile>();
      });
      Mapper = config.CreateMapper();
    }

    public static async Task UpdateAccount(
      [ServiceBusTrigger(ResourceQueueNames.Account)] AccountUpdateMessage message,
      TextWriter log) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        var existing = await context.Accounts
          .Include(x => x.MetaData)
          .SingleOrDefaultAsync(x => x.Id == update.Id);
        if (existing == null) {
          if (update.Type == RealArtists.ShipHub.Common.GitHub.Models.GitHubAccountType.Organization) {
            existing = (Organization)context.Accounts.Add(new Organization());
          } else {
            existing = (Organization)context.Accounts.Add(new User());
          }
          existing.Id = update.Id;
        }
        Mapper.Map(update, existing);

        await context.SaveChangesAsync();
      }
    }

    public static async Task UpdateComment(
      [ServiceBusTrigger(ResourceQueueNames.Comment)] CommentUpdateMessage message,
      TextWriter log) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        var existing = await context.Comments
          .Include(x => x.MetaData)
          .SingleOrDefaultAsync(x => x.Id == update.Id);
        if (existing == null) {
          existing = context.Comments.Add(new Comment() {
            Id = update.Id,
            IssueId = message.IssueId,
            RepositoryId = message.RepositoryId,
          });
        }
        Mapper.Map(update, existing);

        await context.SaveChangesAsync();
      }
    }

    public static async Task UpdateIssue(
      [ServiceBusTrigger(ResourceQueueNames.Issue)] IssueUpdateMessage message,
      TextWriter log) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        var existing = await context.Issues
          .Include(x => x.MetaData)
          .SingleOrDefaultAsync(x => x.Id == update.Id);
        if (existing == null) {
          existing = context.Issues.Add(new Issue() {
            Id = update.Id,
            RepositoryId = message.RepositoryId,
          });
        }
        Mapper.Map(update, existing);

        await context.SaveChangesAsync();
      }
    }

    public static async Task UpdateMilestone(
      [ServiceBusTrigger(ResourceQueueNames.Milestone)] MilestoneUpdateMessage message,
      TextWriter log) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        var existing = await context.Milestones
          .Include(x => x.MetaData)
          .SingleOrDefaultAsync(x => x.Id == update.Id);
        if (existing == null) {
          existing = context.Milestones.Add(new Milestone() {
            Id = update.Id,
            RepositoryId = message.RepositoryId,
          });
        }
        Mapper.Map(update, existing);

        await context.SaveChangesAsync();
      }
    }

    public static async Task UpdateRepository(
      [ServiceBusTrigger(ResourceQueueNames.Repository)] RepositoryUpdateMessage message,
      TextWriter log) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        var existing = await context.Repositories
          .Include(x => x.MetaData)
          .SingleOrDefaultAsync(x => x.Id == update.Id);
        if (existing == null) {
          existing = context.Repositories.Add(new Repository() {
            Id = update.Id,
            AccountId = message.AccountId,
          });
        }
        Mapper.Map(update, existing);

        await context.SaveChangesAsync();
      }
    }
  }
}
