using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using MSOSync.Engine;

namespace MSOSync.Transport;

public static class TransportServiceExtensions
{
    public static IServiceCollection AddTransportServices(
        this IServiceCollection services,
        IConfiguration _)
    {
        // Singletons
        services.AddSingleton<GzipCompressionService>();
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
        services.AddScoped<IApplyService, NoOpApplyService>();

        // SmartTransportService registered as the ITransportService implementation
        // (replaces NoOpTransportService removed from AddSyncEngine in Task 8)
        services.AddScoped<ITransportService, SmartTransportService>();

        return services;
    }
}
