#requires -Version 7.4

# Test, publish, and build the unsigned win-x64 installer from this checkout.
# Signing and Microsoft Store packaging remain separate steps in the guide.

[CmdletBinding()]
param(
    [string]$InnoSetupCompiler
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$Root = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$Configuration = "Release"
$Runtime = "win-x64"
$Project = Join-Path $Root "AnalysisITC.Avalonia\AnalysisITC.Avalonia.csproj"
$CoreTests = Join-Path $Root "AnalysisITC.Core.Tests\AnalysisITC.Core.Tests.csproj"
$AvaloniaTests = Join-Path $Root "AnalysisITC.Avalonia.Tests\AnalysisITC.Avalonia.Tests.csproj"
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$PackageDir = Join-Path $Root "artifacts\packages"
$InstallerDefinition = Join-Path $PSScriptRoot "installer.iss"

[xml]$ProjectXml = Get-Content -LiteralPath $Project -Raw
$Version = ([string]$ProjectXml.Project.PropertyGroup.Version).Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Expected a numeric release version in $Project; found '$Version'."
}
$InstallerBaseName = "FT-ITC-Analysis-$Version-win-x64-setup"
$Installer = Join-Path $PackageDir "$InstallerBaseName.exe"

if (-not $InnoSetupCompiler) {
    $CompilerCommand = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($CompilerCommand) {
        $InnoSetupCompiler = $CompilerCommand.Source
    }
    else {
        $InnoSetupCompiler = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $InnoSetupCompiler -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Inno Setup compiler not found. Install Inno Setup 6 or pass -InnoSetupCompiler "C:\path\to\ISCC.exe".'
}

# A failed build must not leave an older installer at this version's output path.
if (Test-Path -LiteralPath $Installer) {
    Remove-Item -LiteralPath $Installer -Force
}

dotnet test $CoreTests --configuration $Configuration
dotnet test $AvaloniaTests --configuration $Configuration

dotnet restore $Project --runtime $Runtime

if (Test-Path -LiteralPath $PublishDir) {
    $ResolvedPublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
    if (-not $ResolvedPublishDir.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish directory is outside the repository: $ResolvedPublishDir"
    }
    Remove-Item -LiteralPath $ResolvedPublishDir -Recurse -Force
}
dotnet publish $Project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $PublishDir

New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null
& $InnoSetupCompiler /Qp `
    "/DSourceDir=$PublishDir" `
    "/DOutputDir=$PackageDir" `
    "/DAppVersion=$Version" `
    "/DOutputBaseFilename=$InstallerBaseName" `
    "/DAppPublisher=Frederik Theisen" `
    $InstallerDefinition

if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) {
    throw "Inno Setup did not produce the expected installer: $Installer"
}

Write-Host "Published Windows application files to:"
Write-Host $PublishDir
Write-Host "Created unsigned Windows installer for version ${Version}:"
Write-Host $Installer
Write-Host "Install this file to update the installed application."
