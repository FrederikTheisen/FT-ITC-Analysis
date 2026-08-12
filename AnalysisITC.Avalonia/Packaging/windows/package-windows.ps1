[CmdletBinding()]
param(
    [ValidateSet("Direct", "Store", "All")]
    [string]$Channel = "Direct",
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$UnsignedDirect,
    [switch]$NoRestore,
    [switch]$Development
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$Project = Join-Path $Root "AnalysisITC.Avalonia/AnalysisITC.Avalonia.csproj"
$CoreTests = Join-Path $Root "AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj"
$AvaloniaTests = Join-Path $Root "AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj"
$InstallerDefinition = Join-Path $PSScriptRoot "installer.iss"
$Artifacts = Join-Path $Root "artifacts"
$PublishDir = Join-Path $Artifacts "publish/$Runtime"
$StoreStageDir = Join-Path $Artifacts "package/windows-store-$Runtime"
$PackageDir = Join-Path $Artifacts "packages"
$ChecksumFile = Join-Path $PackageDir "SHA256SUMS-windows.txt"
$BuiltArtifacts = [Collections.Generic.List[string]]::new()

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Resolve-RequiredCommand {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) { throw "$Name was not found." }
    return $command.Source
}

function Resolve-FirstExistingPath {
    param([Parameter(Mandatory)][string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Resolve-CommandPath {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Convert-ToInnoSignText {
    param([Parameter(Mandatory)][string]$Value)

    return $Value.Replace('$', '$$')
}

function Resolve-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsRoot -PathType Container)) {
        throw "$Name was not found because the Windows SDK bin directory does not exist."
    }

    $tool = Get-ChildItem -LiteralPath $kitsRoot -Directory |
        Sort-Object { try { [version]$_.Name } catch { [version]"0.0" } } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if (-not $tool) { throw "$Name was not found in the installed Windows SDKs." }
    return $tool
}

function Resolve-InnoCompiler {
    $compiler = Resolve-FirstExistingPath @(
        (Resolve-CommandPath "ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    if (-not $compiler) { throw "ISCC.exe was not found. Install Inno Setup 6." }
    $compilerVersion = (Get-Item -LiteralPath $compiler).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($compilerVersion) -or
        [version]$compilerVersion -lt [version]"6.3" -or
        [version]$compilerVersion -ge [version]"7.0") {
        throw "Inno Setup 6.3 or newer is required. ISCC.exe reported '$compilerVersion'."
    }
    return $compiler
}

function Read-ProjectVersion {
    [xml]$projectXml = Get-Content -LiteralPath $Project -Raw
    $version = [string]$projectXml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) { throw "No Version property found in $Project" }
    if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z.-]+))?$') {
        throw "Project version '$version' is not a supported semantic version."
    }
    $expectedAssemblyVersion = "$($version.Split('-')[0]).0"
    foreach ($propertyName in @("AssemblyVersion", "FileVersion")) {
        $actual = [string]$projectXml.Project.PropertyGroup.$propertyName
        if ($actual -ne $expectedAssemblyVersion) {
            throw "$propertyName '$actual' must equal '$expectedAssemblyVersion' for project version '$version'."
        }
    }
    return $version
}

function Convert-ToMsixVersion {
    param([Parameter(Mandatory)][string]$Version)

    $numericVersion = $Version.Split('-')[0]
    $parts = @($numericVersion.Split('.') | ForEach-Object { [int]$_ })
    while ($parts.Count -lt 4) { $parts += 0 }
    foreach ($part in $parts) {
        if ($part -lt 0 -or $part -gt 65535) {
            throw "MSIX version component '$part' is outside the supported range 0-65535."
        }
    }
    return ($parts[0..3] -join '.')
}

function Assert-ReleaseSource {
    param(
        [Parameter(Mandatory)][string]$Git,
        [Parameter(Mandatory)][string]$Version
    )

    if ($Development) {
        Write-Warning "Development mode: clean-checkout and release-tag guards are disabled."
        return
    }
    if ($Version.Contains('-')) {
        throw "Release packaging does not accept prerelease versions. Use -Development for local packaging."
    }

    $status = (& $Git -C $Root status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect the Git checkout." }
    if ($status) { throw "Release packaging requires a clean Git checkout. Commit or remove local changes first." }

    $expectedTag = "v$Version"
    $tags = @(& $Git -C $Root tag --points-at HEAD)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect tags at HEAD." }
    if ($expectedTag -notin $tags) {
        throw "Release packaging requires HEAD to be tagged '$expectedTag'."
    }
}

function Invoke-CodeSign {
    param(
        [Parameter(Mandatory)][string]$SignTool,
        [Parameter(Mandatory)][string]$Path
    )

    $timestamp = if ($env:FTITC_WINDOWS_TIMESTAMP_URL) {
        $env:FTITC_WINDOWS_TIMESTAMP_URL
    } else {
        "http://timestamp.digicert.com"
    }
    if ($env:FTITC_WINDOWS_CERT_SHA1) {
        Invoke-NativeCommand $SignTool @(
            "sign", "/sha1", $env:FTITC_WINDOWS_CERT_SHA1, "/fd", "SHA256",
            "/tr", $timestamp, "/td", "SHA256", $Path
        )
    } else {
        Invoke-NativeCommand $SignTool @(
            "sign", "/f", $env:FTITC_WINDOWS_SIGNING_CERT,
            "/p", $env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD,
            "/fd", "SHA256", "/tr", $timestamp, "/td", "SHA256", $Path
        )
    }
    Invoke-NativeCommand $SignTool @("verify", "/pa", "/v", $Path)
}

function New-InnoSignCommand {
    param([Parameter(Mandatory)][string]$SignTool)

    $timestamp = if ($env:FTITC_WINDOWS_TIMESTAMP_URL) {
        $env:FTITC_WINDOWS_TIMESTAMP_URL
    } else {
        "http://timestamp.digicert.com"
    }
    $quotedSignTool = '$q' + (Convert-ToInnoSignText $SignTool) + '$q'
    $quotedTimestamp = '$q' + (Convert-ToInnoSignText $timestamp) + '$q'
    if ($env:FTITC_WINDOWS_CERT_SHA1) {
        $thumbprint = Convert-ToInnoSignText $env:FTITC_WINDOWS_CERT_SHA1
        return "$quotedSignTool sign /sha1 $thumbprint /fd SHA256 /tr $quotedTimestamp /td SHA256 `$f"
    }

    if ($env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD -match '["\r\n]') {
        throw "The PFX password cannot contain quotes or line breaks when Inno Setup signing is used. Prefer an installed certificate selected by FTITC_WINDOWS_CERT_SHA1."
    }
    $quotedPfx = '$q' + (Convert-ToInnoSignText $env:FTITC_WINDOWS_SIGNING_CERT) + '$q'
    $quotedPassword = '$q' + (Convert-ToInnoSignText $env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD) + '$q'
    return "$quotedSignTool sign /f $quotedPfx /p $quotedPassword /fd SHA256 /tr $quotedTimestamp /td SHA256 `$f"
}

function New-StorePackage {
    param(
        [Parameter(Mandatory)][string]$MakeAppx,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$MsixVersion
    )

    Remove-Item -LiteralPath $StoreStageDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $StoreStageDir "Assets") | Out-Null
    Copy-Item -Path (Join-Path $PublishDir "*") -Destination $StoreStageDir -Recurse -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "Assets\*") -Destination (Join-Path $StoreStageDir "Assets") -Force

    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "AppxManifest.xml.in") -Raw
    $manifest = $manifest.Replace("@PACKAGE_IDENTITY@", $env:FTITC_WINDOWS_PACKAGE_IDENTITY)
    $manifest = $manifest.Replace("@PUBLISHER@", $env:FTITC_WINDOWS_PUBLISHER)
    $manifest = $manifest.Replace("@PUBLISHER_DISPLAY_NAME@", $env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME)
    $manifest = $manifest.Replace("@VERSION@", $MsixVersion)
    $manifest = $manifest.Replace("@ARCHITECTURE@", "x64")
    if ($manifest -match '@[A-Z_]+@') { throw "The generated AppxManifest.xml contains unresolved placeholders." }

    $manifestPath = Join-Path $StoreStageDir "AppxManifest.xml"
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding utf8NoBOM
    try { [xml](Get-Content -LiteralPath $manifestPath -Raw) | Out-Null }
    catch { throw "Generated AppxManifest.xml is invalid: $($_.Exception.Message)" }

    $msix = Join-Path $PackageDir "FT-ITC-Analysis-$Version-$Runtime-store.msix"
    Remove-Item -LiteralPath $msix -Force -ErrorAction SilentlyContinue
    Invoke-NativeCommand $MakeAppx @("pack", "/d", $StoreStageDir, "/p", $msix, "/o")
    if (-not (Test-Path -LiteralPath $msix -PathType Leaf)) { throw "MakeAppx did not create $msix" }
    $BuiltArtifacts.Add($msix)
}

function New-DirectInstaller {
    param(
        [Parameter(Mandatory)][string]$InnoCompiler,
        [Parameter(Mandatory)][string]$Version,
        [string]$SignTool
    )

    $outputBaseName = "FT-ITC-Analysis-$Version-$Runtime-setup"
    $installer = Join-Path $PackageDir "$outputBaseName.exe"
    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue

    $publisher = if ($env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME) {
        $env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME
    } else {
        "Frederik Theisen"
    }
    $arguments = @(
        "/Qp",
        "/DSourceDir=$PublishDir",
        "/DOutputDir=$PackageDir",
        "/DAppVersion=$Version",
        "/DOutputBaseFilename=$outputBaseName",
        "/DAppPublisher=$publisher"
    )
    if (-not $UnsignedDirect) {
        Invoke-CodeSign -SignTool $SignTool -Path (Join-Path $PublishDir "FT-ITC Analysis.exe")
        $arguments += "/DSignedBuild=1"
        $arguments += "/Sftitc=$(New-InnoSignCommand -SignTool $SignTool)"
    }
    $arguments += $InstallerDefinition

    Invoke-NativeCommand $InnoCompiler $arguments
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
        throw "Inno Setup did not create $installer"
    }
    if (-not $UnsignedDirect) { Invoke-NativeCommand $SignTool @("verify", "/pa", "/v", $installer) }
    $BuiltArtifacts.Add($installer)
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Windows packages must be produced on Windows."
}
$currentBuild = [int](Get-ItemPropertyValue `
    -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" `
    -Name CurrentBuildNumber)
if ($currentBuild -lt 22000) {
    throw "Windows 11 build 22000 or newer is required for packaging. Found build $currentBuild."
}

$dotnet = Resolve-RequiredCommand "dotnet.exe"
$git = Resolve-RequiredCommand "git.exe"
$version = Read-ProjectVersion
$msixVersion = Convert-ToMsixVersion $version
$sdkVersion = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith("10.")) {
    throw "The project requires a .NET 10 SDK. dotnet --version returned '$sdkVersion'."
}
Assert-ReleaseSource -Git $git -Version $version

$buildStore = $Channel -in @("Store", "All")
$buildDirect = $Channel -in @("Direct", "All")
$storeArtifact = Join-Path $PackageDir "FT-ITC-Analysis-$version-$Runtime-store.msix"
$directArtifact = Join-Path $PackageDir "FT-ITC-Analysis-$version-$Runtime-setup.exe"
$expectedArtifacts = @()
if ($buildStore) { $expectedArtifacts += $storeArtifact }
if ($buildDirect) { $expectedArtifacts += $directArtifact }
$makeappx = if ($buildStore) { Resolve-WindowsSdkTool "makeappx.exe" } else { $null }
$innoCompiler = if ($buildDirect) { Resolve-InnoCompiler } else { $null }
$signTool = $null

if ($UnsignedDirect -and -not $buildDirect) {
    throw "-UnsignedDirect is only valid when the Direct channel is being built."
}
if ($buildStore) {
    foreach ($name in @(
        "FTITC_WINDOWS_PACKAGE_IDENTITY",
        "FTITC_WINDOWS_PUBLISHER",
        "FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME"
    )) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            throw "Set $name to the exact value assigned in Microsoft Partner Center."
        }
    }
}
if ($buildDirect -and -not $UnsignedDirect) {
    if (-not $env:FTITC_WINDOWS_CERT_SHA1 -and -not $env:FTITC_WINDOWS_SIGNING_CERT) {
        throw "Configure FTITC_WINDOWS_CERT_SHA1 or FTITC_WINDOWS_SIGNING_CERT, or explicitly pass -UnsignedDirect."
    }
    if (-not $env:FTITC_WINDOWS_CERT_SHA1 -and $env:FTITC_WINDOWS_SIGNING_CERT -and -not $env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD) {
        throw "Set FTITC_WINDOWS_SIGNING_CERT_PASSWORD for the configured PFX."
    }
    if (-not $env:FTITC_WINDOWS_CERT_SHA1 -and $env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD -match '["\r\n]') {
        throw "The PFX password cannot contain quotes or line breaks when Inno Setup signing is used. Prefer an installed certificate selected by FTITC_WINDOWS_CERT_SHA1."
    }
    if (-not $env:FTITC_WINDOWS_CERT_SHA1 -and $env:FTITC_WINDOWS_SIGNING_CERT -and -not (Test-Path -LiteralPath $env:FTITC_WINDOWS_SIGNING_CERT -PathType Leaf)) {
        throw "The configured PFX does not exist: $($env:FTITC_WINDOWS_SIGNING_CERT)"
    }
    $signTool = Resolve-WindowsSdkTool "signtool.exe"
}

New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null
Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
foreach ($artifact in $expectedArtifacts) {
    Remove-Item -LiteralPath $artifact -Force -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $ChecksumFile -Force -ErrorAction SilentlyContinue

try {
    $testArguments = @("test", $CoreTests, "--configuration", $Configuration)
    if ($NoRestore) { $testArguments += "--no-restore" }
    Invoke-NativeCommand $dotnet $testArguments

    $testArguments = @("test", $AvaloniaTests, "--configuration", $Configuration)
    if ($NoRestore) { $testArguments += "--no-restore" }
    Invoke-NativeCommand $dotnet $testArguments

    if (-not $NoRestore) {
        Invoke-NativeCommand $dotnet @("restore", $Project, "--runtime", $Runtime)
    }

    Invoke-NativeCommand $dotnet @(
        "publish", $Project, "--configuration", $Configuration, "--runtime", $Runtime,
        "--self-contained", "true", "--no-restore", "--output", $PublishDir
    )
    $application = Join-Path $PublishDir "FT-ITC Analysis.exe"
    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
        throw "dotnet publish did not create $application"
    }

    if ($buildStore) {
        New-StorePackage -MakeAppx $makeappx -Version $version -MsixVersion $msixVersion
    }
    if ($buildDirect) {
        New-DirectInstaller -InnoCompiler $innoCompiler -Version $version -SignTool $signTool
    }

    $checksumLines = foreach ($artifact in $BuiltArtifacts) {
        $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $artifact)"
    }
    Set-Content -LiteralPath $ChecksumFile -Value $checksumLines -Encoding ascii
    Write-Host "Created:"
    $BuiltArtifacts | ForEach-Object { Write-Host "  $_" }
    Write-Host "  $ChecksumFile"
}
catch {
    foreach ($artifact in $expectedArtifacts) {
        Remove-Item -LiteralPath $artifact -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $ChecksumFile -Force -ErrorAction SilentlyContinue
    throw
}
