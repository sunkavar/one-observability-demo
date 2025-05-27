using System;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Prometheus.DotNetRuntime;
using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PetSite
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Sets default settings to collect dotnet runtime specific metrics
            DotNetRuntimeStatsBuilder.Default().StartCollecting();

            // Configure Activity source for custom spans
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            Activity.ForceDefaultIdFormat = true;

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    Console.WriteLine($"ENVIRONMENT NAME IS: {env.EnvironmentName}");
                    if (env.EnvironmentName.ToLower() == "development")
                        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                            .AddJsonFile($"appsettings.{env.EnvironmentName}.json",
                                optional: true, reloadOnChange: true);
                    else
                        config.AddSystemsManagerWithReload(configureSource =>
                        {
                            configureSource.Path = "/petstore";
                            configureSource.Optional = true;
                            configureSource.ReloadAfter = TimeSpan.FromMinutes(5);
                        });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Add HttpClient factory
                    services.AddHttpClient();
                    
                    // Configure OpenTelemetry
                    services.AddOpenTelemetry()
                        .ConfigureResource(resource => resource
                            .AddService("PetSite"))
                        .WithTracing(tracing => tracing
                            .AddAspNetCoreInstrumentation(options =>
                            {
                                options.RecordException = true;
                                options.EnrichWithHttpRequest = (activity, request) =>
                                {
                                    // Ensure custom attributes are propagated
                                    if (request.RouteValues.TryGetValue("petId", out var petId))
                                    {
                                        activity.SetTag("PetId", petId?.ToString());
                                    }
                                    if (request.RouteValues.TryGetValue("pettype", out var petType))
                                    {
                                        activity.SetTag("PetType", petType?.ToString());
                                    }
                                    if (request.RouteValues.TryGetValue("petcolor", out var petColor))
                                    {
                                        activity.SetTag("PetColor", petColor?.ToString());
                                    }
                                    
                                    // Also check query parameters for these values
                                    if (request.Query.TryGetValue("petId", out var qPetId))
                                    {
                                        activity.SetTag("PetId", qPetId.ToString());
                                    }
                                    if (request.Query.TryGetValue("petType", out var qPetType))
                                    {
                                        activity.SetTag("PetType", qPetType.ToString());
                                    }
                                    if (request.Query.TryGetValue("petColor", out var qPetColor))
                                    {
                                        activity.SetTag("PetColor", qPetColor.ToString());
                                    }
                                };
                            })
                            .AddHttpClientInstrumentation(options =>
                            {
                                options.RecordException = true;
                                options.EnrichWithHttpRequestMessage = (activity, request) =>
                                {
                                    // Propagate attributes from parent span to the HTTP client span
                                    if (Activity.Current != null)
                                    {
                                        foreach (var tag in Activity.Current.Tags)
                                        {
                                            activity.SetTag(tag.Key, tag.Value);
                                        }
                                    }
                                };
                            }));
                })
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}
