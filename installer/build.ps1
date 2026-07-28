<#
    Builds the TrackForge MSI.

    Prerequisites (one time):
        dotnet tool install --global wix --version 5.0.2
        wix extension add -g WixToolset.UI.wixext/5.0.2
        wix extension add -g WixToolset.Util.wixext/5.0.2

    WiX 5 is pinned deliberately: v6 and v7 require accepting the Open Source
    Maintenance Fee EULA, which is a paid licence decision. v5 is free.

    Usage:
        .\installer\build.ps1
        .\installer\build.ps1 -Version 1.1.0
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root       = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $root "src\TrackForge\TrackForge.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$outDir     = Join-Path $PSScriptRoot "out"
$msiPath    = Join-Path $outDir "TrackForge-$Version-x64.msi"

Write-Host "TrackForge installer build" -ForegroundColor Cyan
Write-Host ("-" * 60)

# wix is installed as a global dotnet tool; make sure it's reachable.
$toolsPath = Join-Path $env:USERPROFILE ".dotnet\tools"
if ($env:PATH -notlike "*$toolsPath*") { $env:PATH = "$env:PATH;$toolsPath" }

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix not found. Run: dotnet tool install --global wix --version 5.0.2"
}

# --- publish ------------------------------------------------------------
# Self-contained so the installed app has no .NET prerequisite at all.
Write-Host "`n[1/2] Publishing self-contained $Runtime..." -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -o $publishDir `
    --nologo `
    -v quiet

if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# The pdb would otherwise be swept into the package.
Remove-Item (Join-Path $publishDir "*.pdb") -Force -ErrorAction SilentlyContinue

$exe = Join-Path $publishDir "TrackForge.exe"
if (-not (Test-Path $exe)) { throw "TrackForge.exe was not produced" }
Write-Host ("      TrackForge.exe  {0:N0} MB" -f ((Get-Item $exe).Length / 1MB))

# --- package ------------------------------------------------------------
Write-Host "`n[2/2] Building MSI..." -ForegroundColor Yellow
New-Item -ItemType Directory $outDir -Force | Out-Null

wix build (Join-Path $PSScriptRoot "TrackForge.wxs") `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -d PublishDir="$publishDir" `
    -d ProjectRoot="$root" `
    -arch x64 `
    -o $msiPath

if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

$msi = Get-Item $msiPath
Write-Host ("`nBuilt  {0}" -f $msi.FullName) -ForegroundColor Green
Write-Host ("       {0:N0} MB" -f ($msi.Length / 1MB))
Write-Host "`nInstall with:  msiexec /i `"$($msi.FullName)`""
