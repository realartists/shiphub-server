using System.Web.Http.ExceptionHandling;
using RealArtists.ShipHub.Api.Filters;

namespace RealArtists.ShipHub.Api.Diagnostics {
  public class CommonLogExceptionLogger : ExceptionLogger {
    public override void Log(ExceptionLoggerContext context) {
      var user = context.RequestContext.Principal as ShipHubPrincipal;
      if (user != null) {
        Common.Log.Exception(context.Exception, $"Login={user.Login}, UserId={user.UserId.ToString()}, DebugIdentifier={user.DebugIdentifier}");
      } else {
        Common.Log.Exception(context.Exception);
      }
      
      base.Log(context);
    }
  }
}