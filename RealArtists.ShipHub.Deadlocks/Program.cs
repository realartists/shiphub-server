using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using RealArtists.ShipHub.Common.DataModel;
using RealArtists.ShipHub.Common.DataModel.Types;

namespace RealArtists.ShipHub.Deadlocks {
  class Program {
    static void Main(string[] args) {
      try {
        for (int i = 0; i < 25; ++i) {
          var sw = new Stopwatch();
          sw.Start();
          StressTestMilestonesAndIssues().Wait();
          sw.Stop();
          Console.WriteLine($"Completed in {sw.Elapsed}");
        }
      } catch (AggregateException ae) {
        Console.WriteLine(ae.Flatten());
      } catch (Exception e) {
        Console.WriteLine(e);
      }
      Console.WriteLine("\nPress any key to exit.");
      Console.ReadKey();
    }

    public static IEnumerable<long> UniqueLongs(int seed) {
      var hash = new HashSet<long>();
      var rand = new Random(seed);
      while (true) {
        var temp = rand.Next(1, int.MaxValue);
        if (hash.Add(temp)) {
          yield return temp;
        }
      }
    }

    public static IEnumerable<int> Sequence(int start) {
      int i = start;
      while (true) {
        yield return i;
        ++i;
      }
    }

    public static async Task StressTestMilestonesAndIssues() {
      const int RepoCount = 50; // this is the amount of repos and amount of parallelization
      const int MilestoneCount = 50;
      const int IssueCount = 500;

      var generator = UniqueLongs(0);
      var userName = "stress";
      var userId = generator.First();
      var repoIds = generator.Take(RepoCount).ToArray();
      var milestoneSeq = generator.Take(RepoCount * MilestoneCount).ToArray();
      var issueSeq = generator.Take(RepoCount * IssueCount).ToArray();
      var milestoneIds = repoIds
        .Select((x, idx) => new {
          Id = x,
          MilestoneIds = milestoneSeq.Skip(idx * MilestoneCount).Take(MilestoneCount)
        })
        .ToDictionary(x => x.Id, x => x.MilestoneIds);
      var h = new HashSet<long>();
      foreach (var repoId in repoIds) {
        var mids = milestoneIds[repoId];
        foreach (var mid in mids) {
          if (!h.Add(mid)) {
            Debug.Assert(false);
          }
        }
      }
      var issueIds = repoIds
        .Select((x, idx) => new {
          Id = x,
          IssueIds = issueSeq.Skip(idx * IssueCount).Take(IssueCount)
        })
        .ToDictionary(x => x.Id, x => x.IssueIds);

      try {
        using (var context = new ShipHubContext()) {
          await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] {
            new AccountTableType() {
              Id = userId,
              Login = userName,
              Type = "user",
            }
          });

          await context.BulkUpdateRepositories(DateTimeOffset.UtcNow,
            repoIds.Select(x => new RepositoryTableType() {
              AccountId = userId,
              FullName = $"{userName}/test-repo-{x}",
              Id = x,
              Name = $"test-repo-{x}",
              Private = false,
            }));
        }

        int result = (await Task.WhenAll(repoIds.AsParallel().Select(async (r) => {
          using (var context = new ShipHubContext()) {
            var milestones = milestoneIds[r].Zip(Sequence(1), (id, index) => {
              var ms = new MilestoneTableType();
              ms.Id = id;
              ms.Number = index;
              ms.Title = $"stress{id}.{index}";
              ms.State = "open";
              ms.UpdatedAt = DateTimeOffset.Now;
              ms.CreatedAt = DateTimeOffset.Now;
              return ms;
            });
            Debug.Assert(milestones.Select(m => m.Id).Distinct().Count() == milestones.Count(), "Milestone Ids need to be unique");
            await context.BulkUpdateMilestones(r, milestones, true);
            await context.BulkUpdateMilestones(r, milestones.Take(MilestoneCount / 2), true);
            await context.BulkUpdateMilestones(r, milestones, true);

            
            var checkMilestones = await context.Milestones.Where(x => x.RepositoryId == r).OrderBy(x => x.Id).Select(x => x.Title).ToListAsync();
            var myMilestones = milestones.OrderBy(x => x.Id);
            Debug.Assert(checkMilestones.Count() == milestones.Count());

            var recheck = checkMilestones.Zip(milestones.OrderBy(x => x.Id), (a, b) => {
              if (a != b.Title) {
                Console.WriteLine($"WHat? {a} != {b.Title}");
              }
              return a;
            });
            Debug.Assert(recheck.Count() == myMilestones.Count());

#if false
            var issues = issueIds[r].Zip(Sequence(1), (id, index) => {
              var iss = new IssueTableType();
              iss.Id = id;
              iss.Body = iss.Title = $"Hello World {id}";
              iss.UserId = userId;
              iss.Number = index;
              iss.PullRequest = false;
              iss.State = "open";
              iss.CreatedAt = DateTimeOffset.Now;
              iss.UpdatedAt = DateTimeOffset.Now;
              //iss.MilestoneId = (new Random().Next((int)r * 10 + 1, (int)r * 10 + 1 + MilestoneCount));
              return iss;
            });
            await context.BulkUpdateIssues(r, issues, null, null);
            await context.BulkUpdateIssues(r, issues, null, null);
            await context.BulkUpdateIssues(r, issues, null, null);
#endif
          }
          return IssueCount + MilestoneCount;
        }))).Sum();

        Debug.Assert(result == RepoCount * (IssueCount + MilestoneCount));
      } finally {
        using (var context = new ShipHubContext()) {
          await context.Database.ExecuteSqlCommandAsync(
            $"DELETE FROM SyncLog WHERE OwnerType = 'repo' AND OwnerId IN (SELECT r.Id FROM Repositories as r WHERE r.AccountId = {userId})"
          );
          await context.Database.ExecuteSqlCommandAsync(
            $"DELETE FROM SyncLog WHERE ItemType = 'account' AND ItemId = {userId}"
          );
          await context.Database.ExecuteSqlCommandAsync(
            $"DELETE i FROM Issues as i INNER JOIN Repositories as r ON (r.Id = i.RepositoryId AND r.AccountId = {userId})"
          );
          await context.Database.ExecuteSqlCommandAsync(
            $"DELETE m FROM Milestones as m INNER JOIN Repositories as r ON (r.Id = m.RepositoryId AND r.AccountId = {userId})"
          );
          await context.Database.ExecuteSqlCommandAsync(
            $"DELETE FROM Repositories WHERE AccountId = {userId}"
          );
          await context.Database.ExecuteSqlCommandAsync(
            $"DELETE FROM Accounts WHERE Id = {userId}"
          );
        }
      }
    }
  }
}
