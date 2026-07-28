using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using MSOSync.Engine;

namespace MSOSync.Transport;

public static class TransportServiceExtensions
{
    public static IServiceCollection AddTransportServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Options
        services.Configure<CompressionOptions>(config.GetSection(CompressionOptions.Section));

        // Compression
        services.AddMemoryCache();
        // Register GzipCompressionService as both concrete type (for NodeHttpClient injection,
        // I4 fix) and as ICompressionService (for CompressionNegotiator / default interface use).
        services.AddSingleton<GzipCompressionService>();
        services.AddSingleton<ICompressionService>(sp => sp.GetRequiredService<GzipCompressionService>());
        services.AddSingleton<BrotliCompressionService>();
        services.AddSingleton<ICompressionNegotiator, CompressionNegotiator>();
        services.AddSingleton<ITransportFailureClassifier, TransportFailureClassifier>();

        // Typed HttpClient with Polly resilience
        services.AddHttpClient<NodeHttpClient>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
            });

        services.AddScoped<INodeHttpClient, NodeHttpClient>();

        // Transport services (scoped — one per request / scope)
        services.AddScoped<PushClient>();
        services.AddScoped<PullClient>();
        services.AddScoped<AcknowledgementService>();
        services.AddScoped<ITransportService, SmartTransportService>();

        return services;
    }
}
