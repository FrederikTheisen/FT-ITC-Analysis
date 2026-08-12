param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$Unsigned,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$Project = Join-Path $Root "AnalysisITC.Avalonia/AnalysisITC.Avalonia.csproj"
$Artifacts = Join-Path $Root "artifacts"
$PublishDir = Join-Path $Artifacts "publish/$Runtime"
$StageDir = Join-Path $Artifacts "package/windows-$Runtime"
$PackageDir = Join-Path $Artifacts "packages"

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. Run this script from a Windows SDK/Visual Studio developer shell."
    }
}

function Read-ProjectVersion {
    [xml]$ProjectXml = Get-Content $Project
    $Version = [string]$ProjectXml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "No Version property found in $Project" }
    return $Version
}

function To-MsixVersion([string]$Version) {
    $parts = ($Version.Split('-')[0]).Split('.')
    $numbers = @(0, 0, 0, 0)
    for ($i = 0; $i -lt [Math]::Min($parts.Length, 4); $i++) { $numbers[$i] = [int]$parts[$i] }
    return ($numbers -join '.')
}

function Invoke-CodeSign([string]$Path) {
    $timestamp = if ($env:FTITC_WINDOWS_TIMESTAMP_URL) { $env:FTITC_WINDOWS_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }
    if ($env:FTITC_WINDOWS_CERT_SHA1) {
        & signtool.exe sign /sha1 $env:FTITC_WINDOWS_CERT_SHA1 /fd SHA256 /tr $timestamp /td SHA256 $Path
    }
    else {
        & signtool.exe sign /f $env:FTITC_WINDOWS_SIGNING_CERT /p $env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD /fd SHA256 /tr $timestamp /td SHA256 $Path
    }
    & signtool.exe verify /pa /v $Path
}

Require-Command dotnet
Require-Command makeappx.exe
if (-not $Unsigned) { Require-Command signtool.exe }

$Version = Read-ProjectVersion
$MsixVersion = To-MsixVersion $Version
$Architecture = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64" }
$PackageIdentity = $env:FTITC_WINDOWS_PACKAGE_IDENTITY
$Publisher = $env:FTITC_WINDOWS_PUBLISHER
$PublisherDisplayName = $env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME

if ([string]::IsNullOrWhiteSpace($PackageIdentity)) { $PackageIdentity = "org.ft-itc.Analysis" }
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) { $PublisherDisplayName = "Frederik Theisen" }
if ([string]::IsNullOrWhiteSpace($Publisher)) {
    throw "Set FTITC_WINDOWS_PUBLISHER to the exact certificate or Partner Center publisher subject."
}
if (-not $Unsigned -and -not $env:FTITC_WINDOWS_CERT_SHA1 -and -not $env:FTITC_WINDOWS_SIGNING_CERT) {
    throw "Configure FTITC_WINDOWS_CERT_SHA1 or FTITC_WINDOWS_SIGNING_CERT, or explicitly use -Unsigned for Store-side signing."
}
if (-not $Unsigned -and $env:FTITC_WINDOWS_SIGNING_CERT -and -not $env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD) {
    throw "Set FTITC_WINDOWS_SIGNING_CERT_PASSWORD for the configured PFX."
}

New-Item -ItemType Directory -Force $PackageDir | Out-Null
Remove-Item -Recurse -Force $PublishDir, $StageDir -ErrorAction SilentlyContinue

$publishArgs = @("publish", $Project, "-c", $Configuration, "-r", $Runtime, "--self-contained", "true", "-o", $PublishDir)
if ($NoRestore) { $publishArgs += "--no-restore" }
& dotnet @publishArgs

if (-not $Unsigned) {
    Invoke-CodeSign (Join-Path $PublishDir "FT-ITC Analysis.exe")
}

New-Item -ItemType Directory -Force (Join-Path $StageDir "Assets") | Out-Null
Copy-Item (Join-Path $PublishDir "*") $StageDir -Recurse
Copy-Item (Join-Path $PSScriptRoot "Assets/*") (Join-Path $StageDir "Assets")

$manifest = Get-Content (Join-Path $PSScriptRoot "AppxManifest.xml.in") -Raw
$manifest = $manifest.Replace("@PACKAGE_IDENTITY@", $PackageIdentity)
$manifest = $manifest.Replace("@PUBLISHER@", $Publisher)
$manifest = $manifest.Replace("@PUBLISHER_DISPLAY_NAME@", $PublisherDisplayName)
$manifest = $manifest.Replace("@VERSION@", $MsixVersion)
$manifest = $manifest.Replace("@ARCHITECTURE@", $Architecture)
Set-Content (Join-Path $StageDir "AppxManifest.xml") $manifest -Encoding utf8NoBOM

$Msix = Join-Path $PackageDir "FT-ITC-Analysis-$Version-$Runtime.msix"
& makeappx.exe pack /d $StageDir /p $Msix /o

if (-not $Unsigned) {
    Invoke-CodeSign $Msix
}

Write-Host "Created $Msix"
