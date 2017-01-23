namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Runtime.Serialization;

  public interface IGitHubError {
    string DocumentationUrl { get; }
    IEnumerable<GitHubEntityError> Errors { get; }
    string Message { get; }
    HttpStatusCode Status { get; }
    bool IsAbuse { get; }
  }

  public class GitHubError : IGitHubError {
    public HttpStatusCode Status { get; set; }
    public string Message { get; set; }
    public string DocumentationUrl { get; set; }
    public IEnumerable<GitHubEntityError> Errors { get; set; }
    public bool IsAbuse { get { return Message.Contains("abuse"); } }

    public GitHubException ToException() {
      return new GitHubException(this);
    }
  }

  public class GitHubEntityError {
    public string Resource { get; set; }
    public string Field { get; set; }
    public string Message { get; set; }
    public string DocumentationUrl { get; set; }
    public EntityErrorCode Code { get; set; }
  }

  public enum EntityErrorCode {
    /// <summary>
    /// This means a resource does not exist.
    /// </summary>
    [EnumMember(Value = "custom")]
    Custom,

    /// <summary>
    /// This means a resource does not exist.
    /// </summary>
    [EnumMember(Value = "missing")]
    Missing,

    /// <summary>
    /// This means a required field on a resource has not been set.
    /// </summary>
    [EnumMember(Value = "missing_field")]
    MissingField,

    /// <summary>
    /// This means the formatting of a field is invalid. The documentation for that resource should be able to give you more specific information.
    /// </summary>
    [EnumMember(Value = "invalid")]
    Invalid,

    /// <summary>
    /// This means another resource has the same value as this field. This can happen in resources that must have some unique key (such as Label names).
    /// </summary>
    [EnumMember(Value = "already_exists")]
    AlreadyExists,
  }
}
