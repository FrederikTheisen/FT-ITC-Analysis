# AnalysisITC.Avalonia Packaging

This directory packages the Avalonia application for Windows, Linux, and
macOS. It does not package or modify the original `AnalysisITC.MacOS`
application.

All packages are self-contained .NET publications. Trimming, single-file
publishing, ReadyToRun, and Native AOT remain disabled until they have been
tested with Avalonia, SkiaSharp, reflection, and native library loading.

Generated output is written below `artifacts/`, which is ignored by Git.
Release packages must be built and tested on their target operating system.

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

The first Windows release is x64 only. It can run under x64 emulation on
Windows 11 ARM64. Native ARM64 packaging is deferred until it can be tested on
real ARM64 hardware.

### Prepare a Windows packaging computer

Use Windows 11. Visual Studio, Clang, the WDK, a C++ workload, and a standalone
.NET runtime are not required.

Download `setup-windows-build-host.ps1` from this repository. Open Windows
PowerShell as Administrator and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\setup-windows-build-host.ps1 `
    -CheckoutPath "$env:USERPROFILE\source\FT-ITC-Analysis"
```

The bootstrap script uses WinGet to install:

- Git
- PowerShell 7
- .NET 10 SDK
- Windows 11 SDK, including MakeAppx and SignTool
- Inno Setup 6

It anonymously clones the public repository over HTTPS. No GitHub account or
SSH key is needed. When the checkout already exists, the script verifies its
origin and status but never fetches, pulls, resets, or overwrites it. The
bootstrap is safe to run again after installing updates or restarting Windows.

If WinGet is unavailable, install or repair **Microsoft App Installer** first.
If a newly installed tool is not found, restart Windows and rerun the script.

### Prepare a release checkout

The version comes from `AnalysisITC.Avalonia.csproj`. Before packaging, make
the project version, assembly/file versions, release notes, and Git tag agree.
Commit the release state and create an immutable `v<version>` tag.

On the Windows computer:

```powershell
Set-Location "$env:USERPROFILE\source\FT-ITC-Analysis"
git fetch --tags
git switch --detach "v1.4.1" # replace with the release version
git status --short
```

The final command must print nothing. Release packaging fails when the checkout
is dirty, the project contains a prerelease version, or HEAD lacks the matching
tag. `-Development` bypasses those release guards only for local tests.

Before creating the release tag, exercise the direct packaging path with:

```powershell
pwsh -NoProfile -File `
    .\AnalysisITC.Avalonia\Packaging\windows\package-windows.ps1 `
    -Channel Direct -Runtime win-x64 -UnsignedDirect -Development
```

### Build the direct-download installer

For an unsigned direct installer:

```powershell
pwsh -NoProfile -File `
    .\AnalysisITC.Avalonia\Packaging\windows\package-windows.ps1 `
    -Channel Direct -Runtime win-x64 -UnsignedDirect
```

Unsigned distribution is functional, but Windows displays an unknown publisher
and may show Microsoft Defender SmartScreen warnings. Never describe an unsigned
installer as trusted or signed.

For production signing, prefer a code-signing certificate in the Windows
certificate store:

```powershell
$env:FTITC_WINDOWS_CERT_SHA1 = "CERTIFICATE_THUMBPRINT"
$env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME = "Frederik Theisen"

pwsh -NoProfile -File `
    .\AnalysisITC.Avalonia\Packaging\windows\package-windows.ps1 `
    -Channel Direct -Runtime win-x64
```

A PFX can also be used. Never commit the PFX or its password:

```powershell
$env:FTITC_WINDOWS_SIGNING_CERT = "C:\secure\ft-itc-signing.pfx"
$env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD = "..."

pwsh -NoProfile -File `
    .\AnalysisITC.Avalonia\Packaging\windows\package-windows.ps1 `
    -Channel Direct -Runtime win-x64
```

For Inno Setup command-line signing, the PFX password cannot contain a quote or
line break. An installed certificate selected by thumbprint is preferred and
does not expose a PFX password to the compiler process.

The signing workflow timestamps and verifies the published application. Inno
Setup signs the generated uninstaller and final setup executable, and the
packager independently verifies the setup executable.

The direct installer:

- installs per-user below `%LOCALAPPDATA%\Programs\FT-ITC Analysis`;
- does not require administrator privileges;
- creates a Start-menu shortcut and offers an optional desktop shortcut;
- registers FT-ITC Analysis as an available `.ftxtc` handler;
- uses permanent AppId `{A4F9B601-8F68-459E-9C27-96DDCAA595FB}` for upgrades;
- supports Inno Setup's normal `/SILENT` and `/VERYSILENT` switches; and
- leaves projects, settings, and autosaves intact during uninstall.

### Build the Microsoft Store package

Reserve the product name in Partner Center, then copy the exact values from its
product identity page. Publisher matching is exact and case-sensitive.

```powershell
$env:FTITC_WINDOWS_PACKAGE_IDENTITY = "<Partner Center package identity>"
$env:FTITC_WINDOWS_PUBLISHER = "<Partner Center publisher>"
$env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME = "Frederik Theisen"

pwsh -NoProfile -File `
    .\AnalysisITC.Avalonia\Packaging\windows\package-windows.ps1 `
    -Channel Store -Runtime win-x64
```

Upload the resulting unsigned MSIX to Partner Center. Do not sign that Store
artifact locally; Microsoft signs the package after certification.

To build both channels in one run, configure the Partner Center variables and
either signing credentials or `-UnsignedDirect`, then use `-Channel All`.

### Output and checksums

Depending on the selected channel, packaging creates:

```text
artifacts\packages\FT-ITC-Analysis-<version>-win-x64-store.msix
artifacts\packages\FT-ITC-Analysis-<version>-win-x64-setup.exe
artifacts\packages\SHA256SUMS-windows.txt
```

The checksum file contains only artifacts produced by the current invocation.
Verify a downloaded artifact with:

```powershell
Get-FileHash .\FT-ITC-Analysis-<version>-win-x64-setup.exe -Algorithm SHA256
```

`-NoRestore` is available only when the matching NuGet restore state already
exists. Packaging runs the Core and Avalonia test projects before publishing.

### Windows acceptance checklist

Before publishing either channel:

1. Run the bootstrap on a clean Windows 11 computer, then run it a second time.
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
