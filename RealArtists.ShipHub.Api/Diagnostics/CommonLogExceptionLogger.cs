namespace RealArtists.ShipHub.Api.Diagnostics {
  using System.Diagnostics.CodeAnalysis;
  using System.Web.Http.ExceptionHandling;
  using RealArtists.ShipHub.Api.Filters;

  public class CommonLogExceptionLogger : ExceptionLogger {
    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "UserId")]
    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "DebugIdentifier")]
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
