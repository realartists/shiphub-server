namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Net.Http;
  using RealArtists.ShipHub.Common.GitHub.Models;

  public class GitHubGraphQLRequest : GitHubRequest {
    public const string GraphQLRootPath = "graphql";

    public GitHubGraphQLRequest(string query, RequestPriority priority = RequestPriority.Background) : base(HttpMethod.Post, GraphQLRootPath, priority) {
      Query = query;
      AcceptHeaderOverride = "application/json";
    }

    public string Query { get; private set; }

    public override HttpContent CreateBodyContent() {
      if (Query.IsNullOrWhiteSpace()) { throw new InvalidOperationException("Query cannot be null or whitespace."); }
      if (CacheOptions != null) { throw new InvalidOperationException("GraphQL requests are not cacheable."); }
      if (Method != HttpMethod.Post) { throw new InvalidOperationException("Only POSTs are supported."); }
      if (Path != GraphQLRootPath) { throw new InvalidOperationException($"Path {Path} is not the GraphQL root: {GraphQLRootPath}"); }

      var request = new GraphQLRequest() {
        Query = Query,
      };

      return new ObjectContent<GraphQLRequest>(request, GraphQLSerialization.JsonMediaTypeFormatter, GraphQLSerialization.JsonMediaType);
    }
  }
}
