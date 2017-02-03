namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Runtime.Serialization;

  [Serializable]
  public class GitHubException : Exception {
    public GitHubException() {
    }

    public GitHubException(string message) : base(message) {
    }

    public GitHubException(string message, Exception innerException) : base(message, innerException) {
    }

    public GitHubException(GitHubError error) : base($"{error.Message}\n{error}") {
      Error = error;
    }

    protected GitHubException(SerializationInfo info, StreamingContext context) : base(info, context) {
      if (info != null) {
        Error = info.GetString(nameof(Error)).DeserializeObject<GitHubError>();
      }
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context) {
      base.GetObjectData(info, context);
      info.AddValue(nameof(Error), Error.SerializeObject());
    }

    public GitHubError Error { get; set; }
  }
}
