using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Catalog.Repositories;
using Catalog.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

// dotnet add package AspNetCore.HealthChecks.MongoDb

// docker build -t catalog:v1 .
// docker network create catalognetwork
// docker run -it --rm -p 8080:80 -e MongoDbSettings:Host=mongo -e MongoDbSettings:Password=pass --network=catalognetwork catalog:v1

// docker login
// docker tag catalog:v1 jerboatechnologies/catalog:v1
// docker push jerboatechnologies/catalog:v1

// Pull from docker hub and run it
// docker run -it --rm -p 8080:80 -e MongoDbSettings:Host=mongo -e MongoDbSettings:Password=pass --network=catalognetwork jerboatechnologies/catalog:v1

// Scale Pods
// kubectl scale deployments/catalog-deployment --replicas=3

namespace Catalog
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Serialize types with MongoDb
            BsonSerializer.RegisterSerializer(new GuidSerializer(BsonType.String));
            BsonSerializer.RegisterSerializer(new DateTimeOffsetSerializer(BsonType.String));

            // Getting the Settings Config for MongoDb
            var mongoDbSettings = Configuration.GetSection(nameof(MongoDbSettings)).Get<MongoDbSettings>();

            // Adding the MongoDb Client on App Startup
            services.AddSingleton<IMongoClient>(serviceProvider => 
            {
                return new MongoClient(mongoDbSettings.ConnectionString);
            });

            // Adding the Items Interface to communicate with the MongoDb Items collection
            services.AddSingleton<IItemsRepository, MongoDbItemsRepository>();
            
            // Allowing Asyncronous methods by suppressing .NET 5's ability to remove the Async keyword from the end of function names
            services.AddControllers(options => {
                options.SuppressAsyncSuffixInActionNames = false;
            });

            // Swagger .NET 5 Generation (Added by template)
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Catalog", Version = "v1" });
            });

            // Adding the Health Checks service on app startup
            services.AddHealthChecks()
                // More health checks @ https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks
                .AddMongoDb(
                    mongoDbSettings.ConnectionString, 
                    name: "mongodb", 
                    timeout: TimeSpan.FromSeconds(3), 
                    tags: new[] {"ready"}); // Tags to further break down Health Checks into specific endpoints
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Catalog v1"));
            }

            if (env.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }
            
            app.UseRouting();

            app.UseAuthorization();

            // Adding endpoints to Api via controller files
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                // Adding the /health endpoint for REST Api health checks
                // Adding the /health/ready endpoint for to see if the database is ready to recieve requests
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions {
                    Predicate = (check) => check.Tags.Contains("ready"),

                    // Make response include more data
                    ResponseWriter = async(context, report) => {
                        var result = JsonSerializer.Serialize(
                            new
                            {
                                status = report.Status.ToString(),
                                checks = report.Entries.Select(entry => new
                                {
                                    name = entry.Key,
                                    status = entry.Value.Status.ToString(),
                                    exception = entry.Value.Exception != null ? entry.Value.Exception.Message : "none",
                                    duration = entry.Value.Duration.ToString()
                                })
                            }
                        );
                        // Render as JSON in response
                        context.Response.ContentType = MediaTypeNames.Application.Json;

                        // Write the response
                        await context.Response.WriteAsync(result);
                    }
                });

                // Adding the /health/live endpoint | Includes no health checks, just if the API is "live"
                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = (_) => false
                });
            });
        }
    }
}
