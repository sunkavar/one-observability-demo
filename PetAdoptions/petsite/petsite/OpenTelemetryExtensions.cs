using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using System;
using OpenTelemetry.Exporter;

namespace PetSite
{
    public static class OpenTelemetryExtensions
    {
        public static IServiceCollection AddPetSiteOpenTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            // Get the OTLP endpoint from environment variable or use default
            string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://0.0.0.0:4317";
            
            // Get the OTLP protocol from environment variable or use default
            string otlpProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "grpc";
            OtlpExportProtocol protocol = otlpProtocol.ToLowerInvariant() switch
            {
                "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                "http" => OtlpExportProtocol.HttpProtobuf,
                "grpc" => OtlpExportProtocol.Grpc,
                _ => OtlpExportProtocol.Grpc // Default to gRPC if unrecognized
            };
            
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
                            options.Endpoint = new Uri(otlpEndpoint);
                            options.Protocol = protocol;
                        });
                })
                .WithLogging(loggingProviderBuilder =>
                {
                    loggingProviderBuilder
                        .AddConsoleExporter()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                            options.Protocol = protocol;
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