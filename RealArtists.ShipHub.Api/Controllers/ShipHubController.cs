﻿namespace RealArtists.ShipHub.Api.Controllers {
  using System.Net;
  using System.Runtime.CompilerServices;
  using System.Web.Http;
  using Filters;

  public abstract class ShipHubController : ApiController {
    protected ShipHubPrincipal ShipHubUser => RequestContext.Principal as ShipHubPrincipal;

    public IHttpActionResult Error(
                         string message,
                         HttpStatusCode status = HttpStatusCode.InternalServerError,
                         object details = null,
      [CallerMemberName] string memberName = "",
      [CallerFilePath]   string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0) {

      var error = new {
        Error = new {
          Message = message,
          CallerMemberName = memberName,
          CallerLineNumber = sourceLineNumber,
          CallerFilePath = sourceFilePath,
          Details = details,
        },
      };

      return Content(status, error);
    }
  }
}
