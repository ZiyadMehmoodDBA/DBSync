# Task 3: MSOSync.Templates (Project Templates)

**Status:** Ready  
**Estimated time:** 4 hours  
**Dependencies:** Tasks 1–2 (sample patterns)  
**Blocks:** Task 4 (Portal), Task 5 (Validation)

---

## Summary

Create the `MSOSync.Templates` NuGet package containing two `dotnet new` templates: `msosync-plugin` (basic) and `msosync-plugin-advanced` (config + services). Templates scaffold complete, buildable plugin projects with configurable names and parameters.

---

## Step 3.1 — Create MSOSync.Templates project structure

```powershell
$root = "D:\MSOSync"
$templatesDir = "$root\src\MSOSync.Templates"

New-Item -ItemType Directory -Force "$templatesDir\content\msosync-plugin\.template.config" | Out-Null
New-Item -ItemType Directory -Force "$templatesDir\content\msosync-plugin-advanced\.template.config" | Out-Null

Write-Host "Created template directories"
```

### Step 3.2 — Create MSOSync.Templates.csproj

**File:** `src/MSOSync.Templates/MSOSync.Templates.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageType>Template</PackageType>
    <PackageVersion>1.0.0</PackageVersion>
    <PackageId>MSOSync.Templates</PackageId>
    <Title>MSOSync Plugin Templates</Title>
    <Authors>MSOSync</Authors>
    <Description>dotnet new templates for building MSOSync plugins.</Description>
    <TargetFramework>net9.0</TargetFramework>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <NoDefaultExcludes>true</NoDefaultExcludes>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="content/**/*" Exclude="content/**/*.csproj" PackagePath="content" />
    <Content Include="content/**/*.csproj" PackagePath="content" />
  </ItemGroup>
</Project>
```

### Step 3.3 — Create MSOSync.Templates README.md

**File:** `src/MSOSync.Templates/README.md`

```markdown
# MSOSync.Templates

Official `dotnet new` templates for building MSOSync plugins.

## Installation

From NuGet:
```bash
dotnet new install MSOSync.Templates
```

From local package (development):
```bash
dotnet pack src/MSOSync.Templates/MSOSync.Templates.csproj -o ./artifacts
dotnet new install ./artifacts/MSOSync.Templates.1.0.0.nupkg
```

## Available Templates

### msosync-plugin (Basic)

Scaffolds a minimal plugin extending `PluginBase`.

```bash
dotnet new msosync-plugin --name MyPlugin
```

Parameters:
- `--name` (required): Plugin class name and project name
- `--pluginId`: Reverse-DNS plugin ID (default: `my.plugin`)
- `--author`: Plugin author name (default: `My Organization`)
- `--description`: Short plugin description

### msosync-plugin-advanced (Config + Services)

Scaffolds a plugin with typed configuration binding and service resolution.

```bash
dotnet new msosync-plugin-advanced --name MyCollector --capability Collector
```

Parameters:
- `--name` (required): Plugin class name and project name
- `--pluginId`: Reverse-DNS plugin ID (default: `my.plugin`)
- `--author`: Plugin author name (default: `My Organization`)
- `--description`: Short plugin description
- `--capability`: Plugin capability (`None`, `Collector`, `Transport`, `Operation`)

## Listing Templates

```bash
dotnet new list --tag MSOSync
```

Expected output:
```
Templates                           Short Name              Language  Tags
msosync-plugin                      msosync-plugin          [C#]      MSOSync/Plugin
msosync-plugin-advanced             msosync-plugin-advanced [C#]      MSOSync/Plugin
```
```

---

## Part A: Basic Template (`msosync-plugin`)

### Step 3.4 — Create basic template.json

**File:** `src/MSOSync.Templates/content/msosync-plugin/.template.config/template.json`

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "MSOSync",
  "classifications": ["MSOSync", "Plugin"],
  "identity": "MSOSync.Plugin.Basic",
  "name": "MSOSync Plugin",
  "shortName": "msosync-plugin",
  "tags": {
    "language": "C#",
    "type": "project"
  },
  "sourceName": "MyPlugin",
  "symbols": {
    "pluginId": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "my.plugin",
      "description": "Reverse-DNS plugin identifier (e.g. acme.my-plugin)",
      "replaces": "my.plugin"
    },
    "author": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "My Organization",
      "description": "Plugin author name",
      "replaces": "My Organization"
    },
    "description": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "My MSOSync plugin.",
      "description": "Short plugin description",
      "replaces": "My MSOSync plugin."
    }
  }
}
```

### Step 3.5 — Create basic MyPlugin.cs template

**File:** `src/MSOSync.Templates/content/msosync-plugin/MyPlugin.cs`

```csharp
using MSOSync.Sdk.Hosting;

namespace MyPlugin;

public sealed class MyPlugin : PluginBase
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.LogInformation(
            "{PluginId} started (host: {HostVersion})",
            Context.Metadata.PluginId,
            Context.Environment.HostVersion);

        return Task.CompletedTask;
    }
}
```

### Step 3.6 — Create basic MyPlugin.csproj template

**File:** `src/MSOSync.Templates/content/msosync-plugin/MyPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup Condition="'$(MSOSyncSdkLocal)' == 'true'">
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
  <ItemGroup Condition="'$(MSOSyncSdkLocal)' != 'true'">
    <PackageReference Include="MSOSync.Sdk" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### Step 3.7 — Create basic plugin.json template

**File:** `src/MSOSync.Templates/content/msosync-plugin/plugin.json`

```json
{
  "manifestVersion": 1,
  "id": "my.plugin",
  "name": "MyPlugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "MyPlugin.dll",
  "entryType": "MyPlugin.MyPlugin",
  "author": "My Organization",
  "description": "My MSOSync plugin.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

### Step 3.8 — Create basic plugin.config.json template

**File:** `src/MSOSync.Templates/content/msosync-plugin/plugin.config.json`

```json
{}
```

---

## Part B: Advanced Template (`msosync-plugin-advanced`)

### Step 3.9 — Create advanced template.json

**File:** `src/MSOSync.Templates/content/msosync-plugin-advanced/.template.config/template.json`

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "MSOSync",
  "classifications": ["MSOSync", "Plugin"],
  "identity": "MSOSync.Plugin.Advanced",
  "name": "MSOSync Plugin (Advanced)",
  "shortName": "msosync-plugin-advanced",
  "tags": {
    "language": "C#",
    "type": "project"
  },
  "sourceName": "MyPlugin",
  "symbols": {
    "pluginId": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "my.plugin",
      "description": "Reverse-DNS plugin identifier (e.g. acme.my-plugin)",
      "replaces": "my.plugin"
    },
    "author": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "My Organization",
      "description": "Plugin author name",
      "replaces": "My Organization"
    },
    "description": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "My MSOSync plugin.",
      "description": "Short plugin description",
      "replaces": "My MSOSync plugin."
    },
    "capability": {
      "type": "parameter",
      "datatype": "choice",
      "choices": [
        {
          "choice": "None",
          "description": "No capability declared"
        },
        {
          "choice": "Collector",
          "description": "Data collector plugin"
        },
        {
          "choice": "Transport",
          "description": "Transport/webhook plugin"
        },
        {
          "choice": "Operation",
          "description": "Operations plugin"
        }
      ],
      "defaultValue": "None",
      "description": "Primary plugin capability",
      "replaces": "None"
    }
  }
}
```

### Step 3.10 — Create advanced MyPlugin.cs template

**File:** `src/MSOSync.Templates/content/msosync-plugin-advanced/MyPlugin.cs`

```csharp
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace MyPlugin;

public sealed class MyPlugin : PluginBase
{
    private Timer? _workTimer;
    private MyPluginSettings? _settings;

    public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Context = context;

        // Load and validate settings at initialization
        _settings = LoadSettings();

        Context.Logger.LogInformation(
            "Initializing {PluginId}",
            Context.Metadata.PluginId);

        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("MyPlugin.Start");

        // Try to resolve optional host services
        var factory = Context.Services.GetService<IHttpClientFactory>();
        if (factory != null)
        {
            Context.Logger.LogInformation("Host provides IHttpClientFactory");
        }

        Context.Logger.LogInformation(
            "Starting {PluginId} (host: {HostVersion})",
            Context.Metadata.PluginId,
            Context.Environment.HostVersion);

        // Start background work timer (optional)
        _workTimer = new Timer(
            _ => DoWork(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("MyPlugin.Stop");
        Context.Logger.LogInformation("Stopping {PluginId}", Context.Metadata.PluginId);
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        _workTimer?.Dispose();
        await base.DisposeAsync();
    }

    private MyPluginSettings LoadSettings()
    {
        var configSection = Context.Configuration.GetSection("Config");
        return new MyPluginSettings(
            Enabled: configSection.GetValue("Enabled", true),
            IntervalSeconds: configSection.GetValue("IntervalSeconds", 30));
    }

    private void DoWork()
    {
        try
        {
            Context.Logger.LogDebug("Performing work...");
            
            // Add plugin logic here
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Error during work");
        }
    }
}
```

### Step 3.11 — Create advanced MyPluginSettings.cs template

**File:** `src/MSOSync.Templates/content/msosync-plugin-advanced/MyPluginSettings.cs`

```csharp
namespace MyPlugin;

internal sealed record MyPluginSettings(
    bool Enabled,
    int IntervalSeconds);
```

### Step 3.12 — Create advanced MyPlugin.csproj template

**File:** `src/MSOSync.Templates/content/msosync-plugin-advanced/MyPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup Condition="'$(MSOSyncSdkLocal)' == 'true'">
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
  <ItemGroup Condition="'$(MSOSyncSdkLocal)' != 'true'">
    <PackageReference Include="MSOSync.Sdk" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### Step 3.13 — Create advanced plugin.json template

**File:** `src/MSOSync.Templates/content/msosync-plugin-advanced/plugin.json`

```json
{
  "manifestVersion": 1,
  "id": "my.plugin",
  "name": "MyPlugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "MyPlugin.dll",
  "entryType": "MyPlugin.MyPlugin",
  "author": "My Organization",
  "description": "My MSOSync plugin.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

### Step 3.14 — Create advanced plugin.config.json template

**File:** `src/MSOSync.Templates/content/msosync-plugin-advanced/plugin.config.json`

```json
{
  "Config": {
    "Enabled": true,
    "IntervalSeconds": 30
  }
}
```

---

## Step 3.15 — Add MSOSync.Templates to MSOSync.sln

**File:** `MSOSync.sln`

Open the solution in Visual Studio or a text editor and locate the project section. Add the Templates project under a new `Templates` solution folder:

```
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Templates", "Templates", "{TEMPLATES-GUID}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "MSOSync.Templates", "src\MSOSync.Templates\MSOSync.Templates.csproj", "{TEMPLATES-PROJECT-GUID}"
EndProject
```

Mark the Templates project as excluded from default build by finding the `GlobalSection(ProjectConfigurationPlatforms)` and ensuring it does **not** have a `Build` entry for the Templates project (or explicitly set `{TEMPLATES-PROJECT-GUID}.Release|Any CPU.Build.0 =` with no value).

**Important:** The sample projects under `samples/` are **not** added to the solution.

### Step 3.16 — Verify template structure

```powershell
$root = "D:\MSOSync"
$templatesDir = "$root\src\MSOSync.Templates"

$requiredFiles = @(
  "$templatesDir\MSOSync.Templates.csproj",
  "$templatesDir\README.md",
  "$templatesDir\content\msosync-plugin\.template.config\template.json",
  "$templatesDir\content\msosync-plugin\MyPlugin.cs",
  "$templatesDir\content\msosync-plugin\MyPlugin.csproj",
  "$templatesDir\content\msosync-plugin\plugin.json",
  "$templatesDir\content\msosync-plugin\plugin.config.json",
  "$templatesDir\content\msosync-plugin-advanced\.template.config\template.json",
  "$templatesDir\content\msosync-plugin-advanced\MyPlugin.cs",
  "$templatesDir\content\msosync-plugin-advanced\MyPluginSettings.cs",
  "$templatesDir\content\msosync-plugin-advanced\MyPlugin.csproj",
  "$templatesDir\content\msosync-plugin-advanced\plugin.json",
  "$templatesDir\content\msosync-plugin-advanced\plugin.config.json"
)

$missing = @()
foreach ($file in $requiredFiles) {
  if (Test-Path $file) {
    Write-Host "✓ $([System.IO.Path]::GetFileName($file))"
  } else {
    Write-Host "✗ $file"
    $missing += $file
  }
}

if ($missing.Count -gt 0) {
  Write-Error "Missing files: $missing"
  exit 1
}

Write-Host "`n✓ All template files present"
```

- [ ] All template files created

---

## Step 3.17 — Build and pack the templates project

```powershell
$root = "D:\MSOSync"
$templatesProj = "$root\src\MSOSync.Templates\MSOSync.Templates.csproj"

Write-Host "Building MSOSync.Templates..."
dotnet build $templatesProj

if ($LASTEXITCODE -ne 0) {
  Write-Error "Build failed"
  exit 1
}

Write-Host "Packing MSOSync.Templates..."
dotnet pack $templatesProj -o "$root\artifacts" --no-build

if ($LASTEXITCODE -ne 0) {
  Write-Error "Pack failed"
  exit 1
}

$packagePath = "$root\artifacts\MSOSync.Templates.1.0.0.nupkg"
if (Test-Path $packagePath) {
  Write-Host "✓ Package created: $packagePath"
} else {
  Write-Error "Package not found at $packagePath"
  exit 1
}
```

- [ ] MSOSync.Templates package builds and packs successfully

---

## Step 3.18 — Install and test templates

```powershell
$root = "D:\MSOSync"
$packagePath = "$root\artifacts\MSOSync.Templates.1.0.0.nupkg"
$tempDir = "$env:TEMP\msosync-template-test-$(Get-Random)"

Write-Host "Installing templates from $packagePath..."
dotnet new install $packagePath

Write-Host "Verifying template list..."
dotnet new list --tag MSOSync

Write-Host "Testing basic template..."
New-Item -ItemType Directory -Force $tempDir | Out-Null

$basicTestDir = "$tempDir\BasicTest"
dotnet new msosync-plugin --name TestBasicPlugin --output $basicTestDir --force

if ($LASTEXITCODE -ne 0) {
  Write-Error "Basic template scaffold failed"
  exit 1
}

Write-Host "Building scaffolded basic plugin..."
dotnet build "$basicTestDir\TestBasicPlugin.csproj" /p:MSOSyncSdkLocal=true --warnaserror

if ($LASTEXITCODE -ne 0) {
  Write-Error "Scaffolded basic plugin build failed"
  exit 1
}

Write-Host "✓ Basic template works"

Write-Host "Testing advanced template..."
$advancedTestDir = "$tempDir\AdvancedTest"
dotnet new msosync-plugin-advanced --name TestAdvancedPlugin --capability Collector --output $advancedTestDir --force

if ($LASTEXITCODE -ne 0) {
  Write-Error "Advanced template scaffold failed"
  exit 1
}

Write-Host "Building scaffolded advanced plugin..."
dotnet build "$advancedTestDir\TestAdvancedPlugin.csproj" /p:MSOSyncSdkLocal=true --warnaserror

if ($LASTEXITCODE -ne 0) {
  Write-Error "Scaffolded advanced plugin build failed"
  exit 1
}

Write-Host "✓ Advanced template works"

Write-Host "Uninstalling templates..."
dotnet new uninstall $packagePath

Write-Host "Cleaning up test directory..."
Remove-Item $tempDir -Recurse -Force

Write-Host "`n✓ Template testing complete"
```

- [ ] Both templates scaffold cleanly
- [ ] Scaffolded projects compile with zero errors and zero warnings

---

## Step 3.19 — Final Verification

```powershell
$root = "D:\MSOSync"

Write-Host "Final Verification for Task 3"
Write-Host "================================"

# Check MSOSync.sln was updated
$slnContent = Get-Content "$root\MSOSync.sln" -Raw
if ($slnContent -match 'MSOSync.Templates') {
  Write-Host "✓ MSOSync.Templates added to solution"
} else {
  Write-Error "✗ MSOSync.Templates not found in solution"
  exit 1
}

# Verify templates directory structure
$templates = @(
  "msosync-plugin",
  "msosync-plugin-advanced"
)

foreach ($template in $templates) {
  $templateDir = "$root\src\MSOSync.Templates\content\$template\.template.config"
  if (Test-Path "$templateDir\template.json") {
    Write-Host "✓ $template template.json present"
  } else {
    Write-Error "✗ $template template.json missing"
    exit 1
  }
}

Write-Host "`n✓ Task 3 verification complete"
```

- [ ] MSOSync.Templates added to solution
- [ ] Both template.json files present
- [ ] All required template content files exist

**Next:** Proceed to Task 4 (Developer Portal)
