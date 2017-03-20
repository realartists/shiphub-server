namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Runtime.Serialization;

  [Serializable]
  public class GitHubPoolEmptyException : Exception {
    public GitHubPoolEmptyException() {
    }

    public GitHubPoolEmptyException(string message) : base(message) {
    }

    public GitHubPoolEmptyException(string message, Exception innerException) : base(message, innerException) {
    }

    protected GitHubPoolEmptyException(SerializationInfo info, StreamingContext context) : base(info, context) {
    }
  }
}
