using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace RealArtists.ShipHub.Api.Filters {
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
  public sealed class CommonLogActionFilterAttribute : ActionFilterAttribute {
    public override Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken) {
      if (actionContext.RequestContext.Principal is ShipHubPrincipal user) {
        Common.Log.Info($"{actionContext.Request.RequestUri} - ${user.DebugIdentifier}");
      } else {
        Common.Log.Info($"{actionContext.Request.RequestUri}");
      }
      return base.OnActionExecutingAsync(actionContext, cancellationToken);
    }
  }
}