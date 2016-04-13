namespace RealArtists.ShipHub.Api.GitHub {
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

  [Serializable]
  public class GitHubException : Exception, IGitHubError {
    public GitHubException() {
    }

    public GitHubException(string message) : base(message) {
    }

    public GitHubException(string message, Exception innerException) : base(message, innerException) {
    }

    public GitHubException(GitHubError error) : base(error.Message) {
      DocumentationUrl = error.DocumentationUrl;
      Status = error.Status;
      Errors = error.Errors;
    }

    protected GitHubException(SerializationInfo info, StreamingContext context) : base(info, context) {
      if (info != null) {
        DocumentationUrl = info.GetString(nameof(DocumentationUrl));
        Status = (HttpStatusCode)info.GetInt32(nameof(Status));
        Errors = info.GetString(nameof(Errors)).DeserializeObject<IEnumerable<GitHubEntityError>>();
      }
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context) {
      base.GetObjectData(info, context);
      info.AddValue(nameof(DocumentationUrl), DocumentationUrl);
      info.AddValue(nameof(Status), (int)Status);
      info.AddValue(nameof(Errors), Errors.SerializeObject());
    }

    public string DocumentationUrl { get; set; }

    public IEnumerable<GitHubEntityError> Errors { get; set; }

    public HttpStatusCode Status { get; set; }

    public bool IsAbuse { get { return Message.Contains("abuse"); } }

    string IGitHubError.Message { get { return Message; } }
  }
}
