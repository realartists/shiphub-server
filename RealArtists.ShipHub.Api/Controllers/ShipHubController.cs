namespace RealArtists.ShipHub.Api.Controllers {
  using System.Net;
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System.Web.Http;
  using AutoMapper;
  using Common.DataModel;

  public abstract class ShipHubController : ApiController {
    private ShipHubContext _Context = new ShipHubContext();
    public ShipHubContext Context { get { return _Context; } }
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
        var temp = Interlocked.Exchange(ref _Context, null);
        if (temp != null) {
          temp.Dispose();
        }
      }
      base.Dispose(disposing);
    }
  }
}
