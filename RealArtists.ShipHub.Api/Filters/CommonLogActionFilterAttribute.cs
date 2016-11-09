using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace RealArtists.ShipHub.Api.Filters {
  public class CommonLogActionFilterAttribute : ActionFilterAttribute {
    public override Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken) {
      var user = actionContext.RequestContext.Principal as ShipHubPrincipal;
      if (user != null) {
        Common.Log.Info($"{actionContext.Request.RequestUri} - ${user.DebugIdentifier}");
      } else {
        Common.Log.Info($"{actionContext.Request.RequestUri}");
      }
      return base.OnActionExecutingAsync(actionContext, cancellationToken);
    }
  }
}