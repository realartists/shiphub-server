using System.Web.Http.ExceptionHandling;
using RealArtists.ShipHub.Api.Filters;

namespace RealArtists.ShipHub.Api.Diagnostics {
  public class CommonLogExceptionLogger : ExceptionLogger {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "UserId")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "DebugIdentifier")]
    public override void Log(ExceptionLoggerContext context) {
      if (context.RequestContext.Principal is ShipHubPrincipal user) {
        Common.Log.Exception(context.Exception, $"Login={user.Login}, UserId={user.UserId.ToString()}, DebugIdentifier={user.DebugIdentifier}");
      } else {
        Common.Log.Exception(context.Exception);
      }

      base.Log(context);
    }
  }
}