using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using System;

namespace PetSite
{
    public static class OpenTelemetryExtensions
    {
        public static IServiceCollection AddPetSiteOpenTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService("PetSite", serviceVersion: "1.0.0"))
                .WithTracing(tracerProviderBuilder =>
                {
                    tracerProviderBuilder
                        .AddSource("PetSite")
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddAWSInstrumentation()
                        .AddConsoleExporter()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"));
                        });
                })
                .WithLogging(loggingProviderBuilder =>
                {
                    loggingProviderBuilder
                        .AddConsoleExporter()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"));
                        });
                });

            services.AddLogging(logging =>
            {
                logging.AddOpenTelemetry(options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    options.ParseStateValues = true;
                });
                logging.AddConsole();
            });

            return services;
        }
    }
}