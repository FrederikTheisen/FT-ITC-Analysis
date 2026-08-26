# AnalysisITC.Avalonia Packaging

This directory packages the Avalonia application for Windows, Linux, and
macOS. It does not package or modify the original `AnalysisITC.MacOS`
application.

All packages are self-contained .NET publications. Trimming, single-file
publishing, ReadyToRun, and Native AOT remain disabled until they have been
tested with Avalonia, SkiaSharp, reflection, and native library loading.

Generated output is written below `artifacts/`, which is ignored by Git.
Release packages must be built and tested on their target operating system.
The Linux and macOS packagers run the shared core and Avalonia Release test
suites before publishing files. The Windows packager runs the equivalent tests
in PowerShell. A test failure stops packaging.

## Project file registration

Packages register only `.ftxtc`, the current FT-ITC Analysis project format.
Legacy `.ftitc` projects remain readable through **File > Open**, but packages
do not claim that extension or make FT-ITC Analysis its operating-system
default. Saving an imported legacy project creates an `.ftxtc` file.

Package, bundle, and installer identifiers are permanent update identities.
Do not change them after the first public release.

## Windows release outputs

Windows has two independent distribution channels:

| Channel | Artifact | Signing and updates |
|---|---|---|
| Direct download | Inno Setup `.exe` | Authenticode-signed when credentials are configured; explicitly unsigned otherwise. Users install later releases over the current installation. |
| Microsoft Store | MSIX | Uploaded unsigned to Partner Center; Microsoft certifies and signs it and supplies Store updates. |

The first Windows release is x64 only. The Windows script intentionally stops
after testing and publishing the application. The commands that create and
sign packages are kept here so the release operator can run and inspect them
one at a time.

### Prepare a Windows packaging computer

Windows 10 22H2 x64 or Windows 11 x64 can be used. Visual Studio, Clang, the
WDK, a C++ workload, and a standalone .NET runtime are not required.

For a first build on a personal Windows 10 desktop, follow
[`WINDOWS-10-QUICKSTART.md`](WINDOWS-10-QUICKSTART.md). It installs Git,
PowerShell 7.4, the .NET 10 SDK, Windows SDK, and Inno Setup one command at a
time, then builds and tests an unsigned direct-download installer. No GitHub
account or SSH key is needed.

Ordinary Windows 10 Home and Pro reached end of support in October 2025. Keep a
Windows 10 packaging computer fully patched through ESU, or upgrade it, before
placing production signing credentials on it.

### Prepare a release checkout

The version comes from `AnalysisITC.Avalonia.csproj`. Make the project version,
assembly/file versions, release notes, and Git tag agree. Commit the release
state and create an immutable `v<version>` tag.

On the Windows computer:

```powershell
Set-Location "$env:USERPROFILE\source\FT-ITC-Analysis"
git fetch --tags
git switch --detach "v1.5.0" # replace with the release version
git status --short
git describe --tags --exact-match
```

`git status --short` must print nothing, and `git describe` must print the
expected `v<version>` tag. These are deliberate operator checks; the packaging
script does not hide them in release-policy logic.

Run the tests and publish the self-contained x64 application:

```powershell
pwsh -NoProfile -File .\AnalysisITC.Avalonia\Packaging\windows\package-windows.ps1
```

The script is deliberately short. For development, run it from your current
branch without doing the tag checks above.

### Set packaging variables

Run these lines from the repository root after publishing:

```powershell
$Root = (Get-Location).Path
[xml]$ProjectXml = Get-Content .\AnalysisITC.Avalonia\AnalysisITC.Avalonia.csproj
$Version = [string]$ProjectXml.Project.PropertyGroup.Version
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$PackageDir = Join-Path $Root "artifacts\packages"
$WindowsPackagingDir = Join-Path $Root "AnalysisITC.Avalonia\Packaging\windows"
$Inno = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
$WindowsSdkBin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.28000.0\x64"
$MakeAppx = Join-Path $WindowsSdkBin "makeappx.exe"
$SignTool = Join-Path $WindowsSdkBin "signtool.exe"
New-Item -ItemType Directory -Force $PackageDir
```

If a later Windows SDK is installed, replace `10.0.28000.0` with the directory
name shown under `C:\Program Files (x86)\Windows Kits\10\bin`.

### Build an unsigned direct-download installer

These commands compile the installer without signing it:


```powershell
$DirectBaseName = "FT-ITC-Analysis-$Version-win-x64-setup"
$InstallerDefinition = Join-Path $WindowsPackagingDir "installer.iss"

& $Inno /Qp `
    "/DSourceDir=$PublishDir" `
    "/DOutputDir=$PackageDir" `
    "/DAppVersion=$Version" `
    "/DOutputBaseFilename=$DirectBaseName" `
    "/DAppPublisher=Frederik Theisen" `
    $InstallerDefinition
```

Unsigned distribution is functional, but Windows displays an unknown publisher
and may show Microsoft Defender SmartScreen warnings. Never describe an unsigned
installer as trusted or signed.

### Build a signed direct-download installer

Prefer a code-signing certificate installed in the Windows certificate store.
Set its SHA-1 thumbprint, then run each command:

```powershell
$CertificateThumbprint = "CERTIFICATE_THUMBPRINT"
$TimestampUrl = "http://timestamp.digicert.com"
$ApplicationExe = Join-Path $PublishDir "FT-ITC Analysis.exe"
$DirectBaseName = "FT-ITC-Analysis-$Version-win-x64-setup"
$DirectInstaller = Join-Path $PackageDir "$DirectBaseName.exe"
$InstallerDefinition = Join-Path $WindowsPackagingDir "installer.iss"

& $SignTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $ApplicationExe
& $SignTool verify /pa /v $ApplicationExe

$InnoSignCommand = "`$q$SignTool`$q sign /sha1 $CertificateThumbprint /fd SHA256 /tr `$q$TimestampUrl`$q /td SHA256 `$f"

& $Inno /Qp `
    "/DSourceDir=$PublishDir" `
    "/DOutputDir=$PackageDir" `
    "/DAppVersion=$Version" `
    "/DOutputBaseFilename=$DirectBaseName" `
    "/DAppPublisher=Frederik Theisen" `
    /DSignedBuild=1 `
    "/Sftitc=$InnoSignCommand" `
    $InstallerDefinition

& $SignTool verify /pa /v $DirectInstaller
```

A PFX or managed signing service can also be used, but its exact `signtool`
arguments depend on the certificate provider. Replace the two signing commands
and `$InnoSignCommand` with the provider's documented command. Never commit a
PFX, password, token, or certificate-provider configuration.

The application must be signed before compiling the installer. `SignedBuild`
makes Inno Setup use the same signing command for its uninstaller and final
setup executable.

The direct installer:

- installs per-user below `%LOCALAPPDATA%\Programs\FT-ITC Analysis`;
- does not require administrator privileges;
- creates a Start-menu shortcut and offers an optional desktop shortcut;
- registers FT-ITC Analysis as an available `.ftxtc` handler;
- uses permanent AppId `{A4F9B601-8F68-459E-9C27-96DDCAA595FB}` for upgrades;
- supports Inno Setup's normal `/SILENT` and `/VERYSILENT` switches; and
- leaves projects, settings, and autosaves intact during uninstall.

### Build the unsigned Microsoft Store package

Do this before signing the published application, or rerun
`package-windows.ps1` first so the Store package contains the original unsigned
application. Reserve the product in Partner Center and insert its exact values:

```powershell
$PackageIdentity = "<Partner Center package identity>"
$Publisher = "<Partner Center publisher>"
$PublisherDisplayName = "Frederik Theisen"
$MsixVersion = "$Version.0"
$StoreStageDir = Join-Path $Root "artifacts\package\windows-store-win-x64"
$StoreMsix = Join-Path $PackageDir "FT-ITC-Analysis-$Version-win-x64-store.msix"

Remove-Item -LiteralPath $StoreStageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $StoreStageDir "Assets")
Copy-Item (Join-Path $PublishDir "*") $StoreStageDir -Recurse
Copy-Item (Join-Path $WindowsPackagingDir "Assets\*") (Join-Path $StoreStageDir "Assets")

$Manifest = Get-Content (Join-Path $WindowsPackagingDir "AppxManifest.xml.in") -Raw
$Manifest = $Manifest.Replace("@PACKAGE_IDENTITY@", $PackageIdentity)
$Manifest = $Manifest.Replace("@PUBLISHER@", $Publisher)
$Manifest = $Manifest.Replace("@PUBLISHER_DISPLAY_NAME@", $PublisherDisplayName)
$Manifest = $Manifest.Replace("@VERSION@", $MsixVersion)
$Manifest = $Manifest.Replace("@ARCHITECTURE@", "x64")
[xml]$Manifest | Out-Null
$Manifest | Set-Content (Join-Path $StoreStageDir "AppxManifest.xml") -Encoding utf8NoBOM

& $MakeAppx pack /d $StoreStageDir /p $StoreMsix /o
```

Upload the resulting unsigned MSIX to Partner Center. Do not sign that Store
artifact locally; Microsoft signs the package after certification.

### Create the checksum file

After creating both artifacts, run:

```powershell
$DirectInstaller = Join-Path $PackageDir "FT-ITC-Analysis-$Version-win-x64-setup.exe"
$StoreMsix = Join-Path $PackageDir "FT-ITC-Analysis-$Version-win-x64-store.msix"
$ChecksumFile = Join-Path $PackageDir "SHA256SUMS-windows.txt"

Get-FileHash $DirectInstaller, $StoreMsix -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" } |
    Set-Content $ChecksumFile -Encoding ascii

Get-Content $ChecksumFile
```

If releasing only one channel, remove the other path from the `Get-FileHash`
line. Upload the setup executable and checksum file to GitHub Releases; upload
the MSIX to Partner Center.

### Windows acceptance checklist

Before publishing either channel:

1. Follow the quick-start on the intended Windows 10 or Windows 11 x64 host.
2. Build from the clean tagged release checkout.
3. Install the direct setup as a normal user and launch from the Start menu.
4. Select the application for `.ftxtc`, then open a project by double-clicking it.
5. Confirm `.ftitc` is not registered but still imports through **File > Open**.
6. Open, edit, save, export, print, and recover an autosave.
7. Install a later setup over the earlier version and verify settings and autosaves remain.
8. Uninstall and verify installed files and associations disappear while user projects and `%APPDATA%\AnalysisITC\Avalonia` remain.
9. Inspect the MSIX manifest and run the Windows App Certification Kit.
10. Download the release artifacts again and independently verify hashes and signatures.

### Manual and deferred Windows work

These steps cannot be completed automatically from a macOS checkout:

- Run Windows-native publication, MakeAppx, Inno Setup, installation, upgrade,
  uninstall, Windows App Certification Kit, and SmartScreen acceptance tests.
- Create and verify a Partner Center account; reserve the product; copy its
  identity; complete listing, questionnaire, screenshot, certification, and
  publishing steps.
- Purchase/configure a trusted direct-distribution certificate or signing
  service if direct downloads should avoid unknown-publisher warnings.
- Create and push the immutable release tag and publish GitHub release assets.
- Test and enable native ARM64 packages on an ARM64 Windows machine.

Automatic direct-download updates are intentionally deferred. A later setup
with the same AppId upgrades the existing installation in place.

## Linux DEB

Requirements are a Debian-compatible Linux build host, .NET 10 SDK,
`dpkg-deb`, GnuPG, and a release signing key.

```bash
export FTITC_GPG_KEY_ID="full-key-fingerprint"
AnalysisITC.Avalonia/Packaging/package.sh linux --runtime linux-x64
```

The packager performs a runtime-specific restore. With `--no-restore`, the
matching target such as `net10.0/linux-x64` must already be present in
`AnalysisITC.Avalonia/obj/project.assets.json`.

The output includes the DEB, SHA-256 checksum, and detached OpenPGP signature.
Use `--unsigned` only for local installation tests. A public APT repository
must also sign its repository metadata.

## Avalonia macOS DMG

Requirements are macOS with Xcode command-line tools, .NET 10 SDK, a Developer
ID Application certificate, and an App Store Connect notarization profile.

Store notarization credentials in Keychain once:

```bash
xcrun notarytool store-credentials FTITC-notary \
  --apple-id "APPLE_ID" \
  --team-id "TEAM_ID" \
  --password "APP_SPECIFIC_PASSWORD"
```

Then package, sign, notarize, and staple:

```bash
export FTITC_MAC_BUNDLE_ID="org.ft-itc.Analysis"
export FTITC_MAC_SIGN_IDENTITY="Developer ID Application: Name (TEAMID)"
export FTITC_MAC_NOTARY_PROFILE="FTITC-notary"
AnalysisITC.Avalonia/Packaging/package.sh macos --runtime osx-arm64 --notarize
```

The script signs each Mach-O binary before signing the app and DMG. It never
uses `codesign --deep` to create signatures. Use `--unsigned` only for local
bundle testing.
