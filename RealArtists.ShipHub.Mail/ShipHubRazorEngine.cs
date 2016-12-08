namespace RealArtists.ShipHub.Mail {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Web;
  using Common;
  using RazorEngine;
  using RazorEngine.Configuration;
  using RazorEngine.Templating;

  public class ShipHubRazorEngine : IRazorEngineService {
    private IRazorEngineService _razor;

    private static Lazy<string> _BaseDirectory = new Lazy<string>(() => {
      var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
      var binDir = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
      if (binDir.Exists) {
        return binDir.FullName;
      } else {
        return dir.FullName;
      }
    });
    public static string BaseDirectory { get { return _BaseDirectory.Value; } }

    public ShipHubRazorEngine() {
      var config = new TemplateServiceConfiguration {
        CachingProvider = new DefaultCachingProvider(_ => { }),
        DisableTempFileLocking = true,
        TemplateManager = new ResolvePathTemplateManager(new[] {
          Path.Combine(BaseDirectory, "Views"),
        })
      };
      _razor = RazorEngineService.Create(config);
      PrecompileTemplates();
    }

    bool precompiled = false;
    public void PrecompileTemplates() {
      if (precompiled) {
        return;
      }

      Log.Info("Begin compiling email templates.");
      var baseDir = new DirectoryInfo(Path.Combine(BaseDirectory, "Views"));
      var files = baseDir.GetFileSystemInfos("*.cshtml", SearchOption.TopDirectoryOnly);
      foreach (var file in files) {
        Log.Info($"Compiling {file.Name}");
        _razor.Compile(Path.GetFileNameWithoutExtension(file.Name));
      }
      Log.Info("End compiling email templates.");
      precompiled = true;
    }

    public void AddTemplate(ITemplateKey key, ITemplateSource templateSource) {
      _razor.AddTemplate(key, templateSource);
    }

    public void Compile(ITemplateKey key, Type modelType = null) {
      _razor.Compile(key, modelType);
    }

    public ITemplateKey GetKey(string name, ResolveType resolveType = ResolveType.Global, ITemplateKey context = null) {
      return _razor.GetKey(name, resolveType, context);
    }

    public bool IsTemplateCached(ITemplateKey key, Type modelType) {
      return _razor.IsTemplateCached(key, modelType);
    }

    public void Run(ITemplateKey key, TextWriter writer, Type modelType = null, object model = null, DynamicViewBag viewBag = null) {
      _razor.Run(key, writer, modelType, model, viewBag);
    }

    public void RunCompile(ITemplateKey key, TextWriter writer, Type modelType = null, object model = null, DynamicViewBag viewBag = null) {
      _razor.Run(key, writer, modelType, model, viewBag);
    }

    private bool disposedValue = false; // To detect redundant calls
    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          if (_razor != null) {
            _razor.Dispose();
            _razor = null;
          }
        }

        disposedValue = true;
      }
    }

    // This code added to correctly implement the disposable pattern.
    public void Dispose() {
      // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
      Dispose(true);
      GC.SuppressFinalize(this);
    }
  }
}
