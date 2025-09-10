using System;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Prometheus.DotNetRuntime;
using System.Diagnostics;
using Amazon.Extensions.Configuration.SystemsManager;
using Microsoft.Extensions.DependencyInjection;

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

                    // Add base configuration first
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

                    if (env.EnvironmentName.ToLower() != "development")
                    {
                        Console.WriteLine("[DEBUG] Loading Systems Manager configuration...");
                        
                        // Create AWS options directly from environment
                        var awsOptions = new AWSOptions();
                        var regionFromEnv = Environment.GetEnvironmentVariable("AWS_REGION") ?? 
                                          Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
                                    
                        Console.WriteLine($"[DEBUG] regionfromEnv is: {regionFromEnv}");
                        
                        if (!string.IsNullOrEmpty(regionFromEnv))
                        {
                            try
                            {
                                awsOptions.Region = Amazon.RegionEndpoint.GetBySystemName(regionFromEnv);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR] Invalid AWS region '{regionFromEnv}': {ex.Message}");
                            }
                        }
                        
                        Console.WriteLine($"[DEBUG] AWS Region: {awsOptions.Region?.SystemName ?? "NOT SET"}");

                        config.AddSystemsManager(configureSource =>
                        {
                            configureSource.Path = "/petstore";
                            configureSource.Optional = true;
                            configureSource.ReloadAfter = TimeSpan.FromMinutes(5);
                            configureSource.AwsOptions = awsOptions;
                        });
                        Console.WriteLine("[DEBUG] Systems Manager configuration added.");
                    }
                    else
                    {
                        Console.WriteLine("[DEBUG] Development mode - skipping Systems Manager.");
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    if (context.HostingEnvironment.EnvironmentName.ToLower() != "development")
                    {
                        services.AddDefaultAWSOptions(context.Configuration.GetAWSOptions());
                    }
                })
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}