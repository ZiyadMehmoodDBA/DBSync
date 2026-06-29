using FluentValidation;
using FluentValidation.AspNetCore;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Metadata;
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
    builder.Services.AddSecurity(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

    builder.Services.AddControllers()
        .AddApplicationPart(typeof(AuthController).Assembly);

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
    builder.Services.AddHostedService<AdminBootstrapper>();

    var app = builder.Build();

    // Serve React SPA from wwwroot/ — must be before auth middleware
    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (!app.Environment.IsDevelopment())
        app.UseHsts();

    app.UseExceptionHandler();
    app.UseRateLimiter();
    app.UseSecurityHeaders();
    app.UseAuthentication();
    app.UseNodeTokenAuth();
    app.UseAuthorization();

    app.MapControllers();

    app.MapGet("/health", () => Results.Ok(new { status = "UP", version = "0.1.0" }))
       .WithName("Health")
       .WithTags("System");

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
