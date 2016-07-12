namespace RealArtists.ShipHub.Api.Controllers {
  using System.Net;
  using System.Runtime.CompilerServices;
  using System.Web.Http;
  using AutoMapper;
  using Common.DataModel;
  using Filters;

  public abstract class ShipHubController : ApiController {
    private ShipHubContext _Context = new ShipHubContext();

    protected ShipHubContext Context { get { return _Context; } }
    protected ShipHubPrincipal ShipHubUser { get { return RequestContext.Principal as ShipHubPrincipal; } }

    public IMapper Mapper { get; private set; } = AutoMapperConfig.Mapper;

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
        if (_Context != null) {
          _Context.Dispose();
          _Context = null;
        }
      }
      base.Dispose(disposing);
    }
  }
}
