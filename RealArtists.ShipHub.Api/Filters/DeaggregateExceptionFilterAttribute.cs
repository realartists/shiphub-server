namespace RealArtists.ShipHub.Api.Filters {
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http.Filters;
  using Utilities;

  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
  public class DeaggregateExceptionFilterAttribute : ExceptionFilterAttribute {
    public override void OnException(HttpActionExecutedContext actionExecutedContext) {
      actionExecutedContext.Exception = actionExecutedContext.Exception.Simplify();
      base.OnException(actionExecutedContext);
    }

    public override Task OnExceptionAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken) {
      actionExecutedContext.Exception = actionExecutedContext.Exception.Simplify();
      return base.OnExceptionAsync(actionExecutedContext, cancellationToken);
    }
  }
}
