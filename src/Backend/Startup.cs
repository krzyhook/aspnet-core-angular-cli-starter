﻿namespace AspNetCoreAngularCli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;

    using AspNetCoreAngularCli.Backend.Data;
    using AspNetCoreAngularCli.Backend.Models;
    using AspNetCoreAngularCli.Options;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.Tokens;

    public class Startup
    {
        private readonly IHostingEnvironment environment;
        public IConfigurationRoot Configuration { get; }


        public Startup(IHostingEnvironment env)
        {
            this.environment = env;

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            //Envrionment variable ASPNETCORE_ENVIRONMENT=Development must be defined
            if (env.IsDevelopment())
            {
                // dotnet user-secrets -h
                // SET VALUE: dotnet user-secrets set MySecret ValueOfMySecret
                // LIST VALUES: dotnet user-secrets list
                // %APPDATA%\microsoft\UserSecrets\<userSecretsId>\secrets.json
                builder.AddUserSecrets<Startup>();
            }

            builder.AddEnvironmentVariables();
                
            this.Configuration = builder.Build();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var cert = new X509Certificate2(Path.Combine(this.environment.ContentRootPath, "IdentityServer4Auth.pfx"), "");

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(this.Configuration.GetConnectionString("DefaultConnection")));

            // Add framework services.
            services.AddMvc()
                .AddMvcOptions(options =>
                {
                    options.CacheProfiles.Add("NoCache", new Microsoft.AspNetCore.Mvc.CacheProfile
                    {
                        NoStore = true,
                        Duration = 0
                    });
                });

            // Configure using a sub-section of the appsettings.json file.
            services.Configure<AppOptions>(this.Configuration.GetSection("App"));

            // SET VALUE: dotnet user-secrets set MySecret ValueOfMySecret
            // LIST VALUES: dotnet user-secrets list
            System.Console.WriteLine($"UserSecret value: {this.Configuration["MySecret"]}");
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, ApplicationDbContext dbContext)
        {
            loggerFactory.AddConsole(this.Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();
            var logger = loggerFactory.CreateLogger("default");
            
            app.UseCors(builder =>
                builder.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
            );

            this.ConfigureAuthentication(app, logger);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }

            // support the Routing of Angular2. If the Browser calls a URL which doesn't exists on the server, it could be a Angular route. Especially if the URL doesn't contain a file extension.
            app.Use(async (context, next) =>
            {
                await next();

                var requestPath = context.Request.Path;

                if (context.Response.StatusCode == 404 
                    && requestPath.HasValue 
                    && !Path.HasExtension(requestPath.Value)
                    && !requestPath.Value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                    && !requestPath.Value.StartsWith("/libs/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Request.Path = context.Request.Path = new Microsoft.AspNetCore.Http.PathString("/");
                    //context.Request.Path = "/index.html";
                    context.Response.StatusCode = 200;
                    
                    await next();
                }
            });

            app.UseDefaultFiles();

            // app.UseStaticFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.Remove("Content-Length");
                }
            });

            app.UseMvcWithDefaultRoute();

            //app.UseMvc(routes =>
            //{
            //    routes.MapRoute(
            //        name: "default",
            //        template: "{controller=Home}/{action=Index}/{id?}");
            //});

            DbInitializer.Initialize(dbContext);

            //app.UseIdentity();
        }

        private void ConfigureAuthentication(IApplicationBuilder app, ILogger logger)
        {
            string audience = this.Configuration["MicrosoftIdentity:ClientId"];
            if (audience == "YOUR CLIENT ID")
            {
                logger.LogError("Your appsettings.json has not been updated with the client id (app id) of your application");
                Environment.Exit(1);
            }

            var tokenValidationParameters = new TokenValidationParameters();
            tokenValidationParameters.ValidateIssuer = false;

            var options = new JwtBearerOptions
                              {
                                  Audience = this.Configuration["MicrosoftIdentity:ClientId"],
                                  Authority = this.Configuration["MicrosoftIdentity:Authority"], // https://login.microsoftonline.com/{tenantId}
                                  TokenValidationParameters = tokenValidationParameters,
                                  //RequireHttpsMetadata = false
                              };

            app.UseJwtBearerAuthentication(options);
        }
    }
}
