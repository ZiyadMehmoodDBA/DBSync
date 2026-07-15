using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using MSOSync.Api.Authorization;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.App.Export;
using MSOSync.App.Health;
using MSOSync.App.Workers;
using MSOSync.Common.Health;
using MSOSync.Common.Workers;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Metadata;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Security;
using MSOSync.Topology;
using MSOSync.Transport;
using MSOSync.Trigger;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

int exitCode = 0;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Guard: NodeToken must not be stored in JSON config — env var only
    if (builder.Configuration is Microsoft.Extensions.Configuration.IConfigurationRoot cfgRoot)
    {
        foreach (var provider in cfgRoot.Providers)
        {
            if (provider is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider
                && provider.TryGet("Node:NodeToken", out _))
            {
                throw new InvalidOperationException(
                    "Node:NodeToken must not be stored in JSON config files. " +
                    "Set the MSOSYNC_NODE_TOKEN environment variable instead.");
            }
        }
    }

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddPersistence(builder.Configuration);
    builder.Services.AddDataProtection();
    builder.Services.AddSecurity(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
    builder.Services.AddScoped<INodeAuthorizationService, NodeAuthorizationService>();

    builder.Services.AddControllers()
        .AddApplicationPart(typeof(AuthController).Assembly)
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter()));

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddMetadata(builder.Configuration);
    builder.Services.AddSingleton<IClock, SystemClock>();
    builder.Services.AddTriggerEngine(builder.Configuration);
    builder.Services.AddEventServices();
    builder.Services.AddRoutingServices();
    builder.Services.AddBatchPipeline(builder.Configuration);
    builder.Services.AddSyncEngine(builder.Configuration);
    builder.Services.AddApplyEngine();
    builder.Services.AddSyncScheduler(builder.Configuration);
    builder.Services.Configure<NodeProperties>(builder.Configuration.GetSection("Node"));
    builder.Services.AddTransportServices(builder.Configuration);
    builder.Services.AddTopologyServices();

    builder.Services.AddSignalR()
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.Converters
                .Add(new JsonStringEnumConverter());
        });

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblyContaining<MSOSync.App.SignalR.NodeOperationsPublisher>());

    builder.Services.AddSingleton<IWorkerStatusRegistry, WorkerStatusRegistry>();

    // --- Epic 12C: System Health ---
    builder.Services.AddSingleton<ISystemHealthService, SystemHealthService>();
    builder.Services.AddSingleton<ISystemHealthContributor, WorkerHealthContributor>();
    builder.Services.AddSingleton<ISystemHealthContributor, DatabaseHealthContributor>();
    builder.Services.AddSingleton<ISystemHealthContributor, ApiHealthContributor>();
    builder.Services.AddSingleton<ISystemHealthContributor, SignalRHealthContributor>();
    builder.Services.AddHealthChecks()
        .AddCheck<WorkerHealthCheck>("workers")
        .AddCheck<MSOSync.Plugin.Diagnostics.PluginHealthCheck>("plugins");

    builder.Services.AddHostedService<AdminBootstrapper>();

    // Export jobs
    builder.Services.Configure<ExportOptions>(builder.Configuration.GetSection("Export"));
    builder.Services.AddScoped<IExportJobService, ExportJobService>();
    builder.Services.AddHostedService<ExportJobWorker>();
    builder.Services.AddHostedService<ExportCleanupWorker>();

    // --- Epic 14A: Plugin Host ---
    builder.Services.Configure<MSOSync.Plugin.Models.PluginHostOptions>(opts =>
    {
        opts.PluginsPath = builder.Configuration["PluginHost:PluginsPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "plugins");
        opts.HostVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    });
    builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginRegistry,
        MSOSync.Plugin.Registry.PluginRegistry>();
    builder.Services.AddScoped<MSOSync.Plugin.Abstractions.IPluginStore,
        MSOSync.Persistence.Stores.PluginStore>();
    builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginLoader,
        MSOSync.Plugin.Loading.PluginLoader>();
    builder.Services.AddSingleton<MSOSync.Plugin.Hosting.PluginHost>();
    builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginHost>(sp =>
        sp.GetRequiredService<MSOSync.Plugin.Hosting.PluginHost>());
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<MSOSync.Plugin.Hosting.PluginHost>());

    var app = builder.Build();

    app.UseExceptionHandler();        // must be first to catch exceptions

    // Security headers for ALL responses (including static assets)
    if (!app.Environment.IsDevelopment())
        app.UseHsts();
    app.UseSecurityHeaders();

    // Static files — now get security headers attached
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseNodeTokenAuth();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<MSOSync.App.Hubs.OperationsHub>("/hubs/operations");

    // Health endpoints (no auth required — infrastructure probes)
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false,  // liveness: always returns UP without running checks
        ResponseWriter = async (ctx, _) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"status\":\"UP\"}");
        }
    });
    app.MapHealthChecks("/health/ready");  // readiness: runs all registered IHealthCheck implementations

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // SPA fallback — must be last: serves index.html for all non-API routes
    app.MapFallbackToFile("index.html");

    Log.Information("MSOSync starting on {Env}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (HostAbortedException)
{
    // Rethrow so WebApplicationFactory can intercept the host during integration tests.
    throw;
}
catch (Exception ex)
{
    Log.Fatal(ex, "MSOSync terminated unexpectedly");
    exitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return exitCode;
