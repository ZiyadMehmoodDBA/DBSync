# Task 4: Production Integration — Static Files + MSBuild Publish Target

**Part of:** [Epic 10A Plan](2026-06-29-epic10a-react-foundation.md)

**Goal:** Wire the .NET API to serve the built React SPA as static files, add an MSBuild target that builds the frontend and copies `dist/` to `wwwroot/` on `dotnet publish`, and run full acceptance validation against all 11 criteria.

**Files:**
- Modify: `src/MSOSync.App/Program.cs`
- Modify: `src/MSOSync.App/MSOSync.App.csproj`

**Interfaces:**
- Consumes (from Tasks 1–3): `npm run build` in `src/MSOSync.Frontend/` produces `dist/index.html` + hashed assets
- Produces: `dotnet publish` copies frontend assets to `publish/wwwroot/`; `MapFallbackToFile("index.html")` serves the SPA for all non-API routes

---

- [ ] **Step 1: Add static file middleware to `Program.cs`**

Open `src/MSOSync.App/Program.cs`. The current middleware order starts with:

```csharp
var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseExceptionHandler();
```

Static files must come **before** the exception handler so that asset requests bypass auth middleware entirely. Insert the two new lines immediately after `app.Build()` (before the `UseHsts` check):

```csharp
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
```

The final Program.cs middleware sequence:
```
UseDefaultFiles()       ← new, serves index.html for /
UseStaticFiles()        ← new, serves hashed JS/CSS/assets
UseHsts()               ← existing (non-dev only)
UseExceptionHandler()   ← existing
UseRateLimiter()        ← existing
UseSecurityHeaders()    ← existing
UseAuthentication()     ← existing
UseNodeTokenAuth()      ← existing
UseAuthorization()      ← existing
MapControllers()        ← existing
MapGet("/health", ...)  ← existing
UseSwagger/UI()         ← existing (dev only)
MapFallbackToFile()     ← new, must be last
```

- [ ] **Step 2: Add `wwwroot/` placeholder to keep git tracking**

Create an empty placeholder so the `wwwroot/` directory exists in the repo:

```powershell
New-Item -ItemType Directory -Force src/MSOSync.App/wwwroot
New-Item -ItemType File src/MSOSync.App/wwwroot/.gitkeep
```

Add to `.gitignore` the built contents (but keep the directory):

Open `.gitignore` and verify it does NOT exclude `src/MSOSync.App/wwwroot/`. The publish pipeline writes here at build time; we only commit the `.gitkeep`.

- [ ] **Step 3: Verify .NET still builds cleanly**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings. The new `UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile` calls require no new NuGet packages — they are part of `Microsoft.AspNetCore`.

- [ ] **Step 4: Add MSBuild target to `MSOSync.App.csproj`**

Open `src/MSOSync.App/MSOSync.App.csproj`. Add the following inside the `<Project>` root (after the existing `<PropertyGroup>` blocks):

```xml
<Target Name="PublishFrontend" AfterTargets="Publish">
  <!--
    Builds the React frontend and copies dist/ → wwwroot/ during dotnet publish.
    Future improvement: add Inputs/Outputs for incremental build (skip if sources unchanged).
    For now, npm run build always runs to keep correctness simple.
  -->
  <Exec
    Command="npm run build"
    WorkingDirectory="$(SolutionDir)src/MSOSync.Frontend" />
  <ItemGroup>
    <FrontendFiles Include="$(SolutionDir)src/MSOSync.Frontend/dist/**/*" />
  </ItemGroup>
  <Copy
    SourceFiles="@(FrontendFiles)"
    DestinationFolder="$(PublishDir)wwwroot/%(RecursiveDir)" />
</Target>
```

Note: `$(SolutionDir)` resolves to the directory containing `MSOSync.sln`. On Windows, use forward slashes in paths inside MSBuild `<Exec>` commands.

- [ ] **Step 5: Test `dotnet publish`**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet publish src/MSOSync.App/MSOSync.App.csproj -c Release -o ./publish-test
```

Expected:
- `npm run build` runs inside `src/MSOSync.Frontend/`
- `publish-test/wwwroot/index.html` exists
- `publish-test/wwwroot/assets/` contains hashed JS and CSS files

- [ ] **Step 6: Test SPA fallback via static serving**

This verifies `MapFallbackToFile` serves `index.html` for deep links.

1. Build and copy frontend manually into wwwroot for this test:
```powershell
cd src/MSOSync.Frontend
npm run build
Copy-Item -Recurse -Force dist/* ../MSOSync.App/wwwroot/
cd ../..
```

2. Start the .NET API:
```powershell
dotnet run --project src/MSOSync.App/MSOSync.App.csproj
```

3. Open browser: `http://localhost:5000/topology`

Expected: React app loads (not a 404), then redirects to `/login` because no access token is present. This confirms `MapFallbackToFile` is working.

4. Navigate to `http://localhost:5000/api/v1/topology` — expected: JSON response or 401 (API route, NOT the SPA fallback).

- [ ] **Step 7: Run full Vitest suite**

```powershell
cd src/MSOSync.Frontend
npm test
```

Expected: all 3 test suites pass (AuthProvider, AuthGuard, Axios client).

- [ ] **Step 8: Run full .NET test suite**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests -c Debug
dotnet test tests/MSOSync.IntegrationTests -c Debug
```

Expected: all 144 .NET tests still green. The Program.cs changes must not break existing API behavior.

- [ ] **Step 9: Acceptance validation checklist**

Verify each criterion manually or via the tests above:

```
□ 1. npm run dev starts on :5173, proxies /api to :5000
□ 2. npm run build exits 0 with TypeScript strict mode — no errors
□ 3. Login at /login authenticates against live .NET API
□ 4. After login, all 15 routes reachable via sidebar
□ 5. Hard reload restores session via refresh token in localStorage
□ 6. (Vitest) 5 concurrent 401s trigger exactly 1 refresh call
□ 7. Theme persists hard reload with NO flash of wrong theme
□ 8. Direct navigation to /topology loads app (MapFallbackToFile)
□ 9. All 3 Vitest suites pass
□ 10. npm run lint — 0 errors
□ 11. dotnet publish includes wwwroot/index.html + assets
```

All 11 must be green before committing.

- [ ] **Step 10: Clean up publish-test directory**

```powershell
Remove-Item -Recurse -Force ./publish-test
```

Do NOT commit the publish output.

- [ ] **Step 11: Commit**

```powershell
git add src/MSOSync.App/Program.cs
git add src/MSOSync.App/MSOSync.App.csproj
git add src/MSOSync.App/wwwroot/.gitkeep
git commit -m "feat(10a): wire .NET static file serving and MSBuild publish pipeline"
```
