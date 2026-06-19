# Epic 4 / Task 8: AddMetadata() Extension + Program.cs Wiring

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `MetadataServiceExtensions.AddMetadata()` that registers all 5 metadata services, `IMemoryCache`, and extends the MediatR scan to include the `MSOSync.Metadata` assembly. Wire into `Program.cs` alongside the changes from Tasks 2 and 3.

**Architecture:** `AddMetadata` calls `services.AddMediatR` a second time with the Metadata assembly — MediatR 12 is idempotent for pipeline behaviors and safe to call multiple times with different assemblies. `Program.cs` receives all Task 2+3+8 changes in this step (implementing them cumulatively).

**Tech Stack:** ASP.NET Core 9, MediatR 12.4.1

## Global Constraints

- `MetadataServiceExtensions` in `MSOSync.Metadata` namespace (no `Services` suffix)
- `Program.cs` using order: `AddPersistence → AddSecurity → AddHttpContextAccessor → AddScoped<ICurrentUserService> → AddExceptionHandler → AddProblemDetails → AddMetadata → AddControllers`
- `app.UseExceptionHandler()` must be FIRST middleware call (before `UseSecurityHeaders`)
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Modify: `src/MSOSync.App/Program.cs` — apply Tasks 2+3+8 changes cumulatively

**Interfaces:**
- Consumes: `INodeMetadataService`, `ITriggerMetadataService`, `IRouterMetadataService`, `IChannelMetadataService`, `IParameterMetadataService` (Tasks 4–7), `GlobalExceptionHandler` (Task 3), `ICurrentUserService`/`HttpContextCurrentUserService` (Task 2)
- Produces: `AddMetadata()` extension — consumed by `Program.cs` and `MetadataFixture` (Task 11)

---

- [ ] **Step 1: Create `MetadataServiceExtensions.cs`**

```csharp
// src/MSOSync.Metadata/MetadataServiceExtensions.cs
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Services;

namespace MSOSync.Metadata;

public static class MetadataServiceExtensions
{
    public static IServiceCollection AddMetadata(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMemoryCache();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ParameterMetadataService>());
        services.AddScoped<IParameterMetadataService, ParameterMetadataService>();
        services.AddScoped<INodeMetadataService, NodeMetadataService>();
        services.AddScoped<ITriggerMetadataService, TriggerMetadataService>();
        services.AddScoped<IRouterMetadataService, RouterMetadataService>();
        services.AddScoped<IChannelMetadataService, ChannelMetadataService>();
        return services;
    }
}
```

- [ ] **Step 2: Update `Program.cs` (applies Tasks 2 + 3 + 8 changes)**

Replace the full content of `src/MSOSync.App/Program.cs`:

```csharp
using FluentValidation;
using FluentValidation.AspNetCore;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
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
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddMetadata(builder.Configuration);

    builder.Services.AddControllers()
        .AddApplicationPart(typeof(AuthController).Assembly);

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

    builder.Services.AddHostedService<AdminBootstrapper>();

    var app = builder.Build();

    app.UseExceptionHandler();
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
```

- [ ] **Step 3: Build full solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 4: Run all existing tests — must still pass**

```powershell
dotnet test MSOSync.sln --no-build
```

Expected: all previously passing tests still pass.

- [ ] **Step 5: Commit**

```powershell
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.App/Program.cs
git commit -m "feat(metadata): wire AddMetadata into Program.cs; add ICurrentUserService, GlobalExceptionHandler"
```
