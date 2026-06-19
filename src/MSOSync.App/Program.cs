using Serilog;
using MSOSync.Persistence;

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

    var app = builder.Build();

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
