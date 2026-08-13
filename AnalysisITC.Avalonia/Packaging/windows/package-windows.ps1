#requires -Version 7.4

# This script deliberately does only one job: test and publish the win-x64 app.
# The packaging guide contains the separate, copy-and-paste commands for making
# an unsigned/signed Inno Setup installer and an unsigned Store MSIX.

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

dotnet test $CoreTests --configuration $Configuration
dotnet test $AvaloniaTests --configuration $Configuration

dotnet restore $Project --runtime $Runtime

Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $Project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $PublishDir

Write-Host "Published Windows application files to:"
Write-Host $PublishDir
