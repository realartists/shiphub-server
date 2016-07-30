namespace RealArtists.ShipHub.Api.Controllers {
  using System.Net;
  using System.Runtime.CompilerServices;
  using System.Web.Http;
  using AutoMapper;
  using Common.DataModel;
  using Filters;

  public abstract class ShipHubController : ApiController {
#pragma warning disable UseAutoPropertyFadedToken // Use auto property
#pragma warning disable UseAutoProperty // Use auto property
    private ShipHubContext _context = new ShipHubContext();

    protected ShipHubContext Context { get { return _context; } }
#pragma warning restore UseAutoProperty // Use auto property
#pragma warning restore UseAutoPropertyFadedToken // Use auto property

    protected ShipHubPrincipal ShipHubUser { get { return RequestContext.Principal as ShipHubPrincipal; } }

    public IMapper Mapper { get; } = AutoMapperConfig.Mapper;

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

    protected override void Dispose(bool disposing) {
      if (disposing) {
        if (_context != null) {
          _context.Dispose();
          _context = null;
        }
      }
      base.Dispose(disposing);
    }
  }
}
