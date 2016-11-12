using System.Diagnostics.CodeAnalysis;

namespace RealArtists.ShipHub.Common.GitHub.Models {
  public enum ContentsFileType {
    File,
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Symlink")]
    Symlink,
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Submodule")]
    Submodule,
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Dir")]
    Dir
  };

  public class ContentsFile {
    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    public ContentsFileType Type { get; set; }
    public long? Size { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Sha")]
    public string Sha { get; set; }
  }
}
