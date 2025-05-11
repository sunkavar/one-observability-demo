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
            // Debug environment variables
            Console.WriteLine("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT: " + Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"));
            Console.WriteLine("OTEL_EXPORTER_OTLP_ENDPOINT: " + Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
            Console.WriteLine("OTEL_EXPORTER_OTLP_PROTOCOL: " + Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL"));
            
            // Get the OTLP traces endpoint from environment variable, fall back to general endpoint, or use default
            string otlpTracesEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT");
            if (string.IsNullOrEmpty(otlpTracesEndpoint))
            {
                otlpTracesEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
                if (string.IsNullOrEmpty(otlpTracesEndpoint))
                {
                    otlpTracesEndpoint = "http://cloudwatch-agent.amazon-cloudwatch:4316/v1/traces";
                }
            }
            Console.WriteLine("Using traces endpoint: " + otlpTracesEndpoint);
            
            // Get the OTLP protocol from environment variable or use default
            string otlpProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "http/protobuf";
            Console.WriteLine("Using protocol: " + otlpProtocol);
            OtlpExportProtocol protocol;
            
            // Explicitly handle the protocol conversion
            if (otlpProtocol.ToLowerInvariant() == "http/protobuf" || otlpProtocol.ToLowerInvariant() == "http")
            {
                protocol = OtlpExportProtocol.HttpProtobuf;
                Console.WriteLine("Setting protocol to HttpProtobuf");
            }
            else if (otlpProtocol.ToLowerInvariant() == "grpc")
            {
                protocol = OtlpExportProtocol.Grpc;
                Console.WriteLine("Setting protocol to Grpc");
            }
            else
            {
                // Default to HTTP/protobuf for CloudWatch Agent
                protocol = OtlpExportProtocol.HttpProtobuf;
                Console.WriteLine("Unrecognized protocol. Defaulting to HttpProtobuf");
            }
            
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
                            options.Endpoint = new Uri(otlpTracesEndpoint);
                            options.Protocol = protocol;
                            Console.WriteLine($"Configured OTLP Tracing exporter with endpoint: {options.Endpoint} and protocol: {options.Protocol}");
                        });
                })
                .WithLogging(loggingProviderBuilder =>
                {
                    loggingProviderBuilder
                        .AddConsoleExporter()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpTracesEndpoint);
                            options.Protocol = protocol;
                            Console.WriteLine($"Configured OTLP Logging exporter with endpoint: {options.Endpoint} and protocol: {options.Protocol}");
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
