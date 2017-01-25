namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Runtime.Serialization;

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
