using FluentValidation;
using FluentValidation.AspNetCore;
using MSOSync.Api.Controllers.Auth;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Security;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

int exitCode = 0;

try
{
    var builder = WebApplication.CreateBuilder(args);

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

    builder.Services.AddHostedService<AdminBootstrapper>();

    var app = builder.Build();

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
