namespace RealArtists.ShipHub.Mail {
  using System.Web;
  using RazorEngine.Templating;

  public class ShipHubTemplateBase<T> : TemplateBase<T> {
    // This fixes the "The name 'Context' does not exist in this context' warning.
    // More at: http://razorengine.codeplex.com/discussions/542559s
    public HttpContextBase Context { get; set; }
  }
}