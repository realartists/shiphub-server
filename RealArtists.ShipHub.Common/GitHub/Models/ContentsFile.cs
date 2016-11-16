using System.Diagnostics.CodeAnalysis;

namespace RealArtists.ShipHub.Common.GitHub.Models {
  public enum ContentsFileType {
    Unknown,
    File,
    Symlink,
    Submodule,
    Dir
  };

  public class ContentsFile {
    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    public ContentsFileType Type { get; set; }
    public long? Size { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string Sha { get; set; }
  }
}
