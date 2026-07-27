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