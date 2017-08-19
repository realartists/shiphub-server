namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using Newtonsoft.Json.Linq;

  // http://graphql.org/learn/serving-over-http/#post-request
  public class GraphQLRequest {
    public string Query { get; set; }
    public string OperationName { get; set; }
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public JObject Variables { get; set; }
  }

  // http://facebook.github.io/graphql/#sec-Errors
  public class GraphQLLocation {
    public long Line { get; set; }
    public long Column { get; set; }
  }

  // http://facebook.github.io/graphql/#sec-Errors
  public class GraphQLError {
    public string Message { get; set; }
    public IEnumerable<GraphQLLocation> Locations { get; set; }
    public IEnumerable<JToken> Path { get; set; }
  }

  //http://facebook.github.io/graphql/#sec-Response-Format
  // http://facebook.github.io/graphql/#sec-Data
  public class GraphQLResponse<T> {
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public T Data { get; set; }
    public IEnumerable<GraphQLError> Errors { get; set; }
  }
}
