namespace RealArtists.ShipHub.Api {
  using System;
  using Configuration;
  using DataModel;
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Builder;
  using Microsoft.AspNetCore.Hosting;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Logging;
  using Middleware.ShipHubAuthentication;

  public class Startup {
    public Startup(IHostingEnvironment env) {
      // Set up configuration sources.
      var builder = new ConfigurationBuilder()
          .AddJsonFile("appsettings.json")
          .AddUserSecrets()
          .AddEnvironmentVariables();
      Configuration = builder.Build();
    }

    public IConfigurationRoot Configuration { get; set; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services) {
      // Add framework services.
      services.AddAuthentication(options => {
        options.SignInScheme = ShipHubAuthenticationDefaults.AuthenticationScheme;
      });

      services.AddAuthorization(options => {
        // This is technically the default and not needed, but I want to be explicit.
        options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
      });

      services.AddMvc();

      // Options
      services.AddOptions();
      services.Configure<GitHubOptions>(Configuration.GetSection("GitHub"));

      // Database Connections
      var gitHubConnectionString = Configuration["Data:GitHubConnection:ConnectionString"];
      var shipHubConnectionString = Configuration["Data:ShipHubConnection:ConnectionString"];
      services.AddScoped(_ => new GitHubContext(gitHubConnectionString));
      services.AddScoped(_ => new ShipHubContext(shipHubConnectionString));
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory) {
      loggerFactory.AddConsole(Configuration.GetSection("Logging"));
      loggerFactory.AddDebug();

      app.UseIISPlatformHandler();

      app.UseWebSockets(new WebSocketOptions() {
        KeepAliveInterval = TimeSpan.FromSeconds(30),
      });

      app.UseShipHubAuthentication();

      app.UseMvc();
    }

    // Entry point for the application.
    public static void Main(string[] args) {
      new WebHostBuilder()
        .UseDefaultConfiguration(args)
        .UseServer("Microsoft.AspNetCore.Server.Kestrel")
        .UseIISPlatformHandlerUrl()
        .UseStartup<Startup>()
        .Build()
        .Run();
    }
  }
}
