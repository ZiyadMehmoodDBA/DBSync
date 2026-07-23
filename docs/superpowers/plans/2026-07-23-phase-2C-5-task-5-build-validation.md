# Task 5: Build Validation + Final Commit

**Status:** Ready  
**Estimated time:** 2 hours  
**Dependencies:** Tasks 1–4 (all deliverables)  
**Blocks:** None (final task)

---

## Summary

Create the `samples/build-check.ps1` script, verify all builds pass, validate portal links, and commit everything to git.

---

## Step 5.1 — Create samples/build-check.ps1

**File:** `samples/build-check.ps1`

```powershell
#
# CI build validation script for MSOSync plugin samples.
# Builds all four samples with MSOSyncSdkLocal=true.
# Exits non-zero if any build fails.
#

$ErrorActionPreference = 'Stop'

$samples = @(
    'HelloWorldPlugin',
    'DataCollectorPlugin',
    'WebhookPlugin',
    'ConfigDrivenPlugin'
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$failed = @()

Write-Host "================================"
Write-Host "MSOSync Plugin Samples Build Check"
Write-Host "================================"
Write-Host ""

foreach ($sample in $samples) {
    $proj = Join-Path $root "$sample\$sample.csproj"
    
    if (-not (Test-Path $proj)) {
        Write-Error "Project not found: $proj"
        $failed += $sample
        continue
    }
    
    Write-Host "Building $sample..."
    dotnet build $proj /p:MSOSyncSdkLocal=true --no-incremental --warnaserror
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ $sample FAILED" -ForegroundColor Red
        $failed += $sample
    } else {
        Write-Host "✓ $sample" -ForegroundColor Green
    }
    Write-Host ""
}

Write-Host "================================"
if ($failed.Count -gt 0) {
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
} else {
    Write-Host "All samples built successfully" -ForegroundColor Green
}
Write-Host "================================"
```

- [ ] `samples/build-check.ps1` created

---

## Step 5.2 — Run build validation script

```powershell
$root = "D:\MSOSync"
$script = "$root\samples\build-check.ps1"

Write-Host "Running build validation..."
& $script

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build validation failed"
    exit 1
}

Write-Host "✓ Build validation passed"
```

- [ ] All 4 samples build successfully
- [ ] script exits 0

---

## Step 5.3 — Verify no warnings

```powershell
$root = "D:\MSOSync"
$samples = @(
    "HelloWorldPlugin",
    "DataCollectorPlugin",
    "WebhookPlugin",
    "ConfigDrivenPlugin"
)

Write-Host "Checking for build warnings..."
$anyWarnings = $false

foreach ($sample in $samples) {
    $proj = "$root\samples\$sample\$sample.csproj"
    $output = dotnet build $proj /p:MSOSyncSdkLocal=true --no-incremental 2>&1 | Out-String
    
    if ($output -match 'warning') {
        Write-Error "$sample has warnings:"
        $output | Select-String 'warning' | ForEach-Object { Write-Error "  $_" }
        $anyWarnings = $true
    }
}

if ($anyWarnings) {
    Write-Error "Build warnings detected"
    exit 1
}

Write-Host "✓ All builds are warning-free"
```

- [ ] Zero warnings across all samples

---

## Step 5.4 — Verify portal structure and links

```powershell
$root = "D:\MSOSync"
$portalDir = "$root\docs\developer-portal"

$requiredPages = @(
    "getting-started.md",
    "plugin-lifecycle.md",
    "configuration.md",
    "services.md",
    "permissions.md",
    "packaging.md",
    "publishing.md",
    "api-reference.md"
)

Write-Host "Verifying portal structure..."

$missing = @()
foreach ($page in $requiredPages) {
    $path = "$portalDir\$page"
    if (Test-Path $path) {
        $size = (Get-Item $path).Length
        Write-Host "✓ $page ($size bytes)"
    } else {
        Write-Host "✗ $page NOT FOUND"
        $missing += $page
    }
}

if ($missing.Count -gt 0) {
    Write-Error "Missing portal pages: $($missing -join ', ')"
    exit 1
}

Write-Host "`n✓ All portal pages present"

# Verify no empty files
Write-Host "`nChecking for empty files..."
Get-ChildItem $portalDir -Filter "*.md" | ForEach-Object {
    $size = $_.Length
    if ($size -lt 100) {
        Write-Error "$($_.Name) is too small ($size bytes) — likely empty"
        exit 1
    }
}

Write-Host "✓ All portal pages have content"

# Validate links
Write-Host "`nValidating internal links..."
$brokenLinks = @()

Get-ChildItem $portalDir -Filter "*.md" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $fileName = $_.Name
    
    # Find markdown links: [text](file.md) or [text](path/to/file.md)
    $linkMatches = [regex]::Matches($content, '\[([^\]]+)\]\(([^)]+)\)')
    
    foreach ($match in $linkMatches) {
        $linkTarget = $match.Groups[2].Value
        
        # Skip external links
        if ($linkTarget -match '^https?:|^mailto:') {
            continue
        }
        
        # Skip anchor-only links
        if ($linkTarget -match '^#') {
            continue
        }
        
        # Resolve relative path
        $targetPath = Join-Path $portalDir $linkTarget
        $targetPath = [System.IO.Path]::GetFullPath($targetPath)
        
        if (-not (Test-Path $targetPath)) {
            $brokenLinks += "$fileName → $linkTarget"
        }
    }
}

if ($brokenLinks.Count -gt 0) {
    Write-Error "Broken links found:"
    $brokenLinks | ForEach-Object { Write-Error "  $_" }
    exit 1
}

Write-Host "✓ All portal links are valid"
```

- [ ] All 8 portal pages exist
- [ ] No empty files
- [ ] All internal links resolve

---

## Step 5.5 — Verify MSOSync.Templates in solution

```powershell
$root = "D:\MSOSync"
$slnPath = "$root\MSOSync.sln"

$slnContent = Get-Content $slnPath -Raw

if ($slnContent -match 'MSOSync.Templates') {
    Write-Host "✓ MSOSync.Templates found in solution"
} else {
    Write-Error "MSOSync.Templates not found in MSOSync.sln"
    exit 1
}

# Verify templates project exists
$templatesProj = "$root\src\MSOSync.Templates\MSOSync.Templates.csproj"
if (Test-Path $templatesProj) {
    Write-Host "✓ MSOSync.Templates.csproj exists"
} else {
    Write-Error "MSOSync.Templates.csproj not found"
    exit 1
}
```

- [ ] MSOSync.Templates added to solution
- [ ] MSOSync.Templates.csproj exists

---

## Step 5.6 — Verify no forbidden dependencies in samples

```powershell
$root = "D:\MSOSync"

$samples = @(
    "HelloWorldPlugin",
    "DataCollectorPlugin",
    "WebhookPlugin",
    "ConfigDrivenPlugin"
)

$forbidden = @(
    "MSOSync.Api",
    "MSOSync.Metadata",
    "MSOSync.Plugin",
    "MSOSync.Persistence",
    "MSOSync.Common"
)

Write-Host "Checking for forbidden dependencies..."

$foundForbidden = @()

foreach ($sample in $samples) {
    $csproj = "$root\samples\$sample\$sample.csproj"
    if (-not (Test-Path $csproj)) {
        Write-Error "Project not found: $csproj"
        exit 1
    }
    
    $content = Get-Content $csproj -Raw
    
    foreach ($pkg in $forbidden) {
        if ($content -match $pkg) {
            $foundForbidden += "$sample references $pkg"
        }
    }
}

if ($foundForbidden.Count -gt 0) {
    Write-Error "Forbidden dependencies found:"
    $foundForbidden | ForEach-Object { Write-Error "  $_" }
    exit 1
}

Write-Host "✓ No forbidden dependencies found"
```

- [ ] No sample references forbidden packages

---

## Step 5.7 — Final verification summary

```powershell
$root = "D:\MSOSync"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Phase 2C.5 Pre-Commit Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Samples directory
$samplesDir = "$root\samples"
if (Test-Path $samplesDir) {
    $sampleCount = @(Get-ChildItem $samplesDir -Directory -Exclude "obj" -ErrorAction SilentlyContinue).Count
    Write-Host "✓ Samples directory: $sampleCount plugins" -ForegroundColor Green
}

# 2. Build check script
if (Test-Path "$samplesDir\build-check.ps1") {
    Write-Host "✓ build-check.ps1 present" -ForegroundColor Green
}

# 3. Templates
$templatesDir = "$root\src\MSOSync.Templates"
if (Test-Path "$templatesDir\MSOSync.Templates.csproj") {
    Write-Host "✓ MSOSync.Templates project created" -ForegroundColor Green
}

$basicTemplate = "$templatesDir\content\msosync-plugin\.template.config\template.json"
$advancedTemplate = "$templatesDir\content\msosync-plugin-advanced\.template.config\template.json"

if ((Test-Path $basicTemplate) -and (Test-Path $advancedTemplate)) {
    Write-Host "✓ Both templates present (basic + advanced)" -ForegroundColor Green
}

# 4. Portal
$portalDir = "$root\docs\developer-portal"
$portalFiles = @(Get-ChildItem $portalDir -Filter "*.md" -ErrorAction SilentlyContinue).Count
Write-Host "✓ Developer portal: $portalFiles Markdown files" -ForegroundColor Green

# 5. Plan files
$planDir = "$root\docs\superpowers\plans"
$planFiles = @(Get-ChildItem $planDir -Filter "2026-07-23-phase-2C-5*.md" -ErrorAction SilentlyContinue).Count
Write-Host "✓ Plan files: $planFiles files created" -ForegroundColor Green

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Ready to commit!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
```

- [ ] All deliverables verified

---

## Step 5.8 — Stage and commit

```powershell
$root = "D:\MSOSync"
cd $root

Write-Host "Staging files for commit..."

# Stage plan files
git add docs/superpowers/plans/2026-07-23-phase-2C-5-*.md

# Stage samples
git add samples/

# Stage templates
git add src/MSOSync.Templates/

# Stage portal
git add docs/developer-portal/

# Update solution file (MSOSync.Templates added)
git add MSOSync.sln

# Check what's staged
Write-Host "`nStaged files:"
git diff --cached --name-only

Write-Host "`nCreating commit..."

$commitMsg = @'
feat(2C.5): SDK Samples + Developer Portal

Add four complete plugin samples demonstrating full SDK surface:
- HelloWorldPlugin: minimal lifecycle contract
- DataCollectorPlugin: SQL polling, configuration, timers
- WebhookPlugin: HTTP delivery, optional service resolution
- ConfigDrivenPlugin: typed config binding, hot-reload pattern

Add MSOSync.Templates NuGet package with two dotnet new templates:
- msosync-plugin (basic): minimal scaffolding
- msosync-plugin-advanced (config + services): full-featured scaffolding

Add comprehensive developer portal (8 Markdown pages):
- getting-started.md: 5-minute quick start
- plugin-lifecycle.md: lifecycle phases and contract
- configuration.md: IPluginConfiguration guide
- services.md: IPluginServices guide  
- permissions.md: permission model
- packaging.md: creating .msopkg archives
- publishing.md: marketplace submission
- api-reference.md: all SDK interfaces documented

Add samples/build-check.ps1 for CI validation of all samples.

All samples compile with zero warnings. No forbidden dependencies.
Both templates scaffold and compile cleanly.
Portal links validated.

Addresses spec: docs/superpowers/specs/2026-07-23-phase-2C-5-sdk-samples-portal.md

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@

git commit -m $commitMsg

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Commit successful" -ForegroundColor Green
    $hash = git rev-parse --short HEAD
    Write-Host "Commit hash: $hash" -ForegroundColor Cyan
} else {
    Write-Error "Commit failed"
    exit 1
}
```

- [ ] All files staged and committed

---

## Step 5.9 — Verify commit

```powershell
$root = "D:\MSOSync"
cd $root

Write-Host "Verifying commit..."

git log --oneline -1
git show --stat --name-count

Write-Host "`n✓ Phase 2C.5 committed successfully"
```

- [ ] Commit hash visible in log
- [ ] All new files included in commit

---

## Final Checklist

All completion criteria from the spec:

- [ ] **All four samples compile with `dotnet build --warnaserror` against `MSOSync.Sdk`**
- [ ] **No sample references `MSOSync.Api`, `MSOSync.Metadata`, `MSOSync.Plugin`, or `MSOSync.Persistence`**
- [ ] **`dotnet new install MSOSync.Templates` completes without error**
- [ ] **Both templates scaffold a directory that compiles with zero errors and zero warnings**
- [ ] **All eight portal Markdown files exist under `docs/developer-portal/` with no broken internal links**
- [ ] **CI build step (`samples/build-check.ps1`) builds all four samples in sequence and exits non-zero on any failure**

---

## Manual Validation Gate (Before Merge)

Before merging the 2C.5 branch:

1. **Live Plugin Load Test**
   - [ ] At least one developer has followed `getting-started.md` from scratch
   - [ ] Plugin loaded successfully on a real MSOSync host instance
   - [ ] Plugin lifecycle logged correctly in host logs

2. **Template Installation Test**
   - [ ] Both templates installed on a clean machine
   - [ ] Scaffolding succeeded
   - [ ] Scaffolded projects compile

3. **Portal Review**
   - [ ] All eight pages reviewed for broken links
   - [ ] Content verified against actual SDK interfaces
   - [ ] Examples are accurate and compile

---

## Post-Merge Checklist

After merge:

- [ ] CI pipeline passes (including build-check.ps1)
- [ ] NuGet package `MSOSync.Templates` prepared for publication (not published yet)
- [ ] PR is marked as ready for release documentation

---

## Notes

- **Spec reference:** `docs/superpowers/specs/2026-07-23-phase-2C-5-sdk-samples-portal.md`
- **Commit includes:** All plan files, all samples, all templates, all portal pages
- **No breaking changes:** Only additions (no modifications to existing `MSOSync.sln` projects)
- **Ready for:** Manual validation before merge; then release & marketplace publication

