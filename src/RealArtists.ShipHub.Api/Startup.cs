namespace RealArtists.ShipHub.Api {
  using Configuration;
  using DataModel;
  using Microsoft.AspNet.Builder;
  using Microsoft.AspNet.Hosting;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Logging;

  public class Startup {
    public Startup(IHostingEnvironment env) {
      // Set up configuration sources.
      var builder = new ConfigurationBuilder()
          .AddJsonFile("appsettings.json")
          .AddEnvironmentVariables();
      Configuration = builder.Build();
    }

    public IConfigurationRoot Configuration { get; set; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services) {
      // Add framework services.
      services.AddMvc();

      // Database Connections
      var gitHubConnectionString = Configuration["Data:GitHubConnection:ConnectionString"];
      var shipHubConnectionString = Configuration["Data:ShipHubConnection:ConnectionString"];
      services.AddScoped(_ => new GitHubContext(gitHubConnectionString));
      services.AddScoped(_ => new ShipHubContext(shipHubConnectionString));

      services.AddOptions();
      services.Configure<GitHubOptions>(Configuration.GetSection("GitHub"));
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory) {
      loggerFactory.AddConsole(Configuration.GetSection("Logging"));
      loggerFactory.AddDebug();

      app.UseIISPlatformHandler();

      app.UseStaticFiles();

      app.UseMvc();
    }

    // Entry point for the application.
    public static void Main(string[] args) => WebApplication.Run<Startup>(args);
  }
}
