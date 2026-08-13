[CmdletBinding()]
param(
    [string]$CheckoutPath = (Join-Path $env:USERPROFILE "source\FT-ITC-Analysis")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryUrl = "https://github.com/FrederikTheisen/FT-ITC-Analysis.git"
$RequiredPackages = @(
    @{ Id = "Git.Git"; Name = "Git" },
    @{ Id = "Microsoft.PowerShell"; Name = "PowerShell 7" },
    @{ Id = "Microsoft.DotNet.SDK.10"; Name = ".NET 10 SDK" },
    @{ Id = "Microsoft.WindowsSDK.10.0.28000"; Name = "Windows 11 SDK" },
    @{ Id = "JRSoftware.InnoSetup"; Name = "Inno Setup 6" }
)

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

function Refresh-ProcessPath {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = @($machinePath, $userPath) -join ";"
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

function Resolve-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsRoot -PathType Container)) { return $null }

    return Get-ChildItem -LiteralPath $kitsRoot -Directory |
        Sort-Object { try { [version]$_.Name } catch { [version]"0.0" } } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "This script must be run on Windows 11."
}

$currentBuild = [int](Get-ItemPropertyValue `
    -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" `
    -Name CurrentBuildNumber)
if ($currentBuild -lt 22000) {
    throw "Windows 11 build 22000 or newer is required for the packaging host. Found build $currentBuild."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this setup script from an elevated PowerShell window (Run as administrator)."
}

$winget = Resolve-CommandPath "winget.exe"
if (-not $winget) {
    throw "WinGet was not found. Install or repair Microsoft App Installer, then run this script again."
}

foreach ($package in $RequiredPackages) {
    & $winget list --id $package.Id --exact --source winget --accept-source-agreements --disable-interactivity | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "$($package.Name) is already installed."
        continue
    }

    Write-Host "Installing $($package.Name)..."
    Invoke-NativeCommand $winget @(
        "install", "--id", $package.Id, "--exact", "--source", "winget", "--scope", "machine", "--silent",
        "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
    )
}

Refresh-ProcessPath

$git = Resolve-FirstExistingPath @(
    (Resolve-CommandPath "git.exe"),
    (Join-Path $env:ProgramFiles "Git\cmd\git.exe")
)
$pwsh = Resolve-FirstExistingPath @(
    (Resolve-CommandPath "pwsh.exe"),
    (Join-Path $env:ProgramFiles "PowerShell\7\pwsh.exe")
)
$dotnet = Resolve-FirstExistingPath @(
    (Resolve-CommandPath "dotnet.exe"),
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
)
$makeappx = Resolve-WindowsSdkTool "makeappx.exe"
$signtool = Resolve-WindowsSdkTool "signtool.exe"
$iscc = Resolve-FirstExistingPath @(
    (Resolve-CommandPath "ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
)

$missing = @()
if (-not $git) { $missing += "git.exe" }
if (-not $pwsh) { $missing += "pwsh.exe" }
if (-not $dotnet) { $missing += "dotnet.exe" }
if (-not $makeappx) { $missing += "makeappx.exe" }
if (-not $signtool) { $missing += "signtool.exe" }
if (-not $iscc) { $missing += "ISCC.exe" }
if ($missing.Count -gt 0) {
    throw "Installation completed, but these required tools could not be located: $($missing -join ', '). Restart Windows and run this script again."
}

$sdkVersion = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith("10.")) {
    throw "The repository requires a .NET 10 SDK. dotnet --version returned '$sdkVersion'."
}
$powerShellVersion = (& $pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()').Trim()
if ($LASTEXITCODE -ne 0 -or [version]$powerShellVersion -lt [version]"7.4") {
    throw "PowerShell 7.4 or newer is required. pwsh reported '$powerShellVersion'. Run 'winget upgrade --id Microsoft.PowerShell --exact'."
}
$windowsSdkVersion = Split-Path (Split-Path $makeappx -Parent) -Leaf
$innoVersion = (Get-Item -LiteralPath $iscc).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($innoVersion) -or
    [version]$innoVersion -lt [version]"6.3" -or
    [version]$innoVersion -ge [version]"7.0") {
    throw "Inno Setup 6.3 or newer is required. ISCC.exe reported '$innoVersion'. Run 'winget upgrade --id JRSoftware.InnoSetup --exact'."
}

$checkoutFullPath = [IO.Path]::GetFullPath($CheckoutPath)
if (Test-Path -LiteralPath $checkoutFullPath) {
    $gitDirectory = Join-Path $checkoutFullPath ".git"
    if (-not (Test-Path -LiteralPath $gitDirectory -PathType Container)) {
        throw "CheckoutPath exists but is not a Git checkout: $checkoutFullPath"
    }

    $origin = (& $git -C $checkoutFullPath remote get-url origin).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not read the existing checkout's origin remote." }
    $acceptedOrigins = @(
        $RepositoryUrl,
        "https://github.com/FrederikTheisen/FT-ITC-Analysis",
        "git@github.com:FrederikTheisen/FT-ITC-Analysis.git"
    )
    if ($origin -notin $acceptedOrigins) {
        throw "The existing checkout has an unexpected origin '$origin'. Nothing was changed."
    }

    Invoke-NativeCommand $git @("-C", $checkoutFullPath, "status", "--short")
    Write-Host "Verified existing checkout without fetching, pulling, resetting, or overwriting it."
}
else {
    $parent = Split-Path -Parent $checkoutFullPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Invoke-NativeCommand $git @("clone", $RepositoryUrl, $checkoutFullPath)
}

Write-Host ""
Write-Host "Windows packaging host is ready."
Write-Host "  Windows build: $currentBuild"
Write-Host "  Git:           $(& $git --version)"
Write-Host "  PowerShell:    $powerShellVersion"
Write-Host "  .NET SDK:      $sdkVersion"
Write-Host "  Windows SDK:   $windowsSdkVersion"
Write-Host "  MakeAppx:      $makeappx"
Write-Host "  SignTool:      $signtool"
Write-Host "  Inno Setup:    $innoVersion ($iscc)"
Write-Host "  Checkout:      $checkoutFullPath"
Write-Host ""
Write-Host "No GitHub account or SSH key was used; the public repository was cloned over HTTPS."
