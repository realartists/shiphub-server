namespace RealArtists.ShipHub.Mail {
  using RazorGenerator.Templating;

  public class ShipHubTemplateBase<T> : RazorTemplateBase {
    public T Model { get; set; }
    public bool SkipHeaderFooter { get; set; }
    public string PreHeader { get; set; }
  }
}
