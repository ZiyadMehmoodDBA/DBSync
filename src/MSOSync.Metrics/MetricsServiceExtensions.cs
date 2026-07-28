// src/MSOSync.Metrics/MetricsServiceExtensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MSOSync.Metrics;

public static class MetricsServiceExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<TelemetryOptions>()
            .BindConfiguration(TelemetryOptions.Section)
            .ValidateOnStart();

        var opts = config.GetSection(TelemetryOptions.Section).Get<TelemetryOptions>() ?? new();

        if (!opts.Enabled)
        {
            // Community Edition default: keep InMemoryMetricsService (already registered elsewhere)
            return services;
        }

        // Replace InMemoryMetricsService with OtelMetricsService
        // Remove any existing IMetricsService registration if present, then add OtelMetricsService
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMetricsService));
        if (descriptor is not null) services.Remove(descriptor);
        services.AddSingleton<IMetricsService, OtelMetricsService>();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(opts.ServiceName, serviceVersion: opts.ServiceVersion))
            .WithMetrics(b => b
                .AddMeter("MSOSync")
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter())
            .WithTracing(b =>
            {
                b.AddAspNetCoreInstrumentation()
                 .AddEntityFrameworkCoreInstrumentation()
                 .AddSource("MSOSync.Pipeline");

                if (!string.IsNullOrEmpty(opts.OtlpEndpoint))
                    b.AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpEndpoint));
            });

        return services;
    }

    public static IApplicationBuilder UseTelemetry(this WebApplication app)
    {
        var opts = app.Configuration.GetSection(TelemetryOptions.Section).Get<TelemetryOptions>() ?? new();
        if (opts.Enabled)
            app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}
