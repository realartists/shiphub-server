namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using g = GitHub.Models;

  /// <summary>
  /// NOT THREAD SAFE!!!!
  /// </summary>
  public class UpdateBatch {
    // \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
    // Batches
    // \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\

    private class BatchDetails {
      public HashSet<long> Accounts { get; set; } = new HashSet<long>();
      public HashSet<long> CommitStatuses { get; set; } = new HashSet<long>();
      public HashSet<long> Issues { get; set; }
      public HashSet<long> IssueComments { get; set; }
      public HashSet<long> Labels { get; set; }

      public bool IsSubsetOf(BatchDetails other) {
        return
             Accounts.IsSubsetOf(other.Accounts)
          && CommitStatuses.IsSubsetOf(other.CommitStatuses)
          && Issues.IsSubsetOf(other.Issues)
          && IssueComments.IsSubsetOf(other.IssueComments)
          && Labels.IsSubsetOf(other.Labels)
          && false; // TODO: Remove once logic below is complete and validated.
      }
    }

    private BatchDetails _batch = new BatchDetails();
    private List<BatchDetails> _batches = new List<BatchDetails>();
    private Dictionary<BatchDetails, Action<bool>> _batchHandlers = new Dictionary<BatchDetails, Action<bool>>();

    public void Checkpoint(Action<bool> handler) {
      if (_batchHandlers.ContainsKey(_batch)) {
        throw new InvalidOperationException("Each batch can have at most one success handler.");
      }
      _batches.Add(_batch);
      _batchHandlers[_batch] = handler;
      _batch = new BatchDetails();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Accounts
    // //////////////////////////////////////////////////////////////////////////////////////////

    private class AccountInfo {
      public DateTimeOffset Date { get; set; }
      public g.Account Account { get; set; }
    }

    private Dictionary<long, AccountInfo> _accounts = new Dictionary<long, AccountInfo>();

    public void AddAccount(g.Account account, DateTimeOffset date) {
      if (account == null) { return; }

      var current = _accounts.Val(account.Id);
      if (current == null || current?.Date < date) {
        _batch.Accounts.Add(account.Id);
        _accounts[account.Id] = new AccountInfo() {
          Account = account,
          Date = date,
        };
      }
    }

    public void AddAccounts(IEnumerable<g.Account> accounts, DateTimeOffset date) {
      if (accounts == null) { return; }

      foreach (var account in accounts) {
        AddAccount(account, date);
      }
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Statuses
    // //////////////////////////////////////////////////////////////////////////////////////////

    private static readonly KeyEqualityComparer<g.CommitStatus> _CommitStatusComparer = KeyEqualityComparer<g.CommitStatus>.FromKeySelector(status => status.Id);
    private Dictionary<long, Dictionary<string, HashSet<g.CommitStatus>>> _commitStatuses = new Dictionary<long, Dictionary<string, HashSet<g.CommitStatus>>>();

    public void AddCommitStatuses(IEnumerable<g.CommitStatus> commitStatuses, long repositoryId, string reference, DateTimeOffset date) {
      if (commitStatuses == null) { return; }

      // Accounts
      AddAccounts(commitStatuses.Select(x => x.Creator).Distinct(x => x.Id), date);

      // Statuses
      var refs = _commitStatuses.Valn(repositoryId);
      var set = refs.Vald(reference, () => new HashSet<g.CommitStatus>(_CommitStatusComparer));
      _batch.CommitStatuses.UnionWith(commitStatuses.Select(x => x.Id));
      set.UnionWith(commitStatuses);
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Issues
    // //////////////////////////////////////////////////////////////////////////////////////////

    private class IssueInfo {
      public long RepositoryId { get; set; }
      public g.Issue Issue { get; set; }
    }

    private Dictionary<long, IssueInfo> _issues = new Dictionary<long, IssueInfo>();

    public void AddIssue(g.Issue issue, long repositoryId, DateTimeOffset date) {
      if (issue == null) { return; }

      // Accounts
      AddAccount(issue.User, date);
      AddAccount(issue.ClosedBy, date);
      AddAccounts(issue.Assignees, date);

      // Milestone
      AddMilestone(issue.Milestone, repositoryId);

      // Labels
      AddLabels(issue.Labels, repositoryId);

      // Issue
      var current = _issues.Val(issue.Id);
      if (current == null || current?.Issue?.UpdatedAt < issue.UpdatedAt) {
        _batch.Issues.Add(issue.Id);
        _issues[issue.Id] = new IssueInfo() {
          Issue = issue,
          RepositoryId = repositoryId,
        };
      }
    }

    public void AddIssues(IEnumerable<g.Issue> issues, long repositoryId, DateTimeOffset date) {
      if (issues == null) { return; }

      foreach (var issue in issues) {
        AddIssue(issue, repositoryId, date);
      }
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Issue Comments
    // //////////////////////////////////////////////////////////////////////////////////////////

    private class IssueCommentInfo {
      public long RepositoryId { get; set; }
      public g.IssueComment IssueComment { get; set; }
    }

    private Dictionary<long, IssueCommentInfo> _issueComments = new Dictionary<long, IssueCommentInfo>();

    public void AddIssueComment(g.IssueComment issueComment, long repositoryId, DateTimeOffset date) {
      var current = _issueComments.Val(issueComment.Id);
      if (current == null || current?.IssueComment?.UpdatedAt < issueComment.UpdatedAt) {
        AddAccount(issueComment.User, date);

        _batch.IssueComments.Add(issueComment.Id);
        _issueComments[issueComment.Id] = new IssueCommentInfo() {
          IssueComment = issueComment,
          RepositoryId = repositoryId,
        };
      }
    }

    public void AddIssueComments(IEnumerable<g.IssueComment> issueComments, long repositoryId, DateTimeOffset date) {
      foreach (var issueComment in issueComments) {
        AddIssueComment(issueComment, repositoryId, date);
      }
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Issue Events
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddIssueEvent(g.IssueEvent issueEvent, long userId, long repositoryId, bool fromTimeline = true) {
      throw new NotImplementedException();
    }

    public void AddIssueEvents(IEnumerable<g.IssueEvent> issueEvents, long userId, long repositoryId, bool fromTimeline = true) {
      throw new NotImplementedException();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Labels
    // //////////////////////////////////////////////////////////////////////////////////////////

    private class LabelCollection {
      public bool Complete { get; set; }
      public Dictionary<long, g.Label> Labels { get; set; }
    }

    private Dictionary<long, LabelCollection> _labels = new Dictionary<long, LabelCollection>();

    public void AddLabel(g.Label label, long repositoryId) {
      _batch.Labels.Add(label.Id);
      var repo
    }

    public void AddLabels(IEnumerable<g.Label> labels, long repositoryId) {
      foreach (var label in labels) {
        AddLabel(label, repositoryId);
      }
    }

    public void SetLabels(IEnumerable<g.Label> labels, long repositoryId) {
      // Always replace existing values if complete
      if (complete) {
        _labels[repositoryId] = new LabelCollection() {
          Complete = true,
          Labels = labels.ToDictionary(x => x.Id, x => x),
        };
      } else {

      }
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // Milestones
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddMilestone(g.Milestone milestone, long repositoryId) {
      throw new NotImplementedException();
    }

    public void AddMilestones(IEnumerable<g.Milestone> milestone, long repositoryId, bool complete = false) {
      throw new NotImplementedException();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // 
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddProject(g.Project project, long? repositoryId = null, long? organizationId = null) {
      throw new NotImplementedException();
    }

    public void AddProjects(IEnumerable<g.Project> projects, long? repositoryId = null, long? organizationId = null) {
      throw new NotImplementedException();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // 
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddPullRequest(g.PullRequest pullRequest, long repositoryId) {
      throw new NotImplementedException();
    }

    public void AddPullRequests(IEnumerable<g.PullRequest> pullRequests, long repositoryId) {
      throw new NotImplementedException();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // 
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddPullRequestComment(g.PullRequestComment comment, long repositoryId, long issueId, long? pendingReviewId = null) {
      throw new NotImplementedException();
    }

    public void AddPullRequestComments(IEnumerable<g.PullRequestComment> comments, long repositoryId, long issueId, long? pendingReviewId = null) {
      throw new NotImplementedException();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // 
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddIssueReactions(IEnumerable<g.Reaction> reactions, long repositoryId, long issueId) {
      throw new NotImplementedException();
    }

    public void AddIssueCommentReactions(IEnumerable<g.Reaction> reactions, long repositoryId, long commentId) {
      throw new NotImplementedException();
    }

    public void AddPullRequestCommentReactions(IEnumerable<g.Reaction> reactions, long repositoryId, long pullRequestCommentId) {
      throw new NotImplementedException();
    }

    public void AddCommitCommentReactions(IEnumerable<g.Reaction> reactions, long repositoryId, long pullRequestCommentId) {
      throw new NotImplementedException();
    }

    // //////////////////////////////////////////////////////////////////////////////////////////
    // 
    // //////////////////////////////////////////////////////////////////////////////////////////

    public void AddReviews(IEnumerable<g.Review> reviews, long repositoryId, long issueId, long userId, DateTimeOffset date) {
      throw new NotImplementedException();
    }
  }
}
