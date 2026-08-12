# AnalysisITC.Avalonia Packaging

This directory packages the Avalonia application for Windows, Linux, and
macOS. Package identities and signing credentials are intentionally not stored
in the repository.

It does not package or modify `AnalysisITC.MacOS`. The Xamarin.Mac application
retains its existing plist, entitlements, signing, notarization, and DMG
workflow. The Avalonia macOS target here is optional; Windows and Linux are the
primary cross-platform distribution targets.

The scripts create self-contained .NET publications. Trimming, single-file
publishing, ReadyToRun, and Native AOT are intentionally disabled until each
mode has been tested with Avalonia, SkiaSharp, reflection, and native library
loading.

## Release outputs

| Target | Output | Signing |
|---|---|---|
| Windows | MSIX | Authenticode certificate through `signtool`, or Microsoft Store signing |
| Linux | DEB | Detached OpenPGP signature and SHA-256 checksum |
| macOS | `.app` in a DMG | Developer ID signing, optional notarization and stapling |

Generated output is written under `artifacts/`, which is ignored by Git.

Package on the target operating system. Although `dotnet publish` can often
cross-publish, native package validation and signing tools are platform-owned.

## File associations

All three package definitions advertise `.ftxtc` as the current FT-ITC Analysis project format and `.ftitc` as the legacy project format.
The application handles command-line paths on Windows and Linux and Avalonia
file-activation events on macOS. Additional raw data formats should remain
available through Open until their ownership and activation behavior have been
decided explicitly.

The package and bundle identifiers are update identities. Confirm them before
the first public release and do not change them afterward.

## Windows MSIX

Requirements:

- Windows 10 or 11
- .NET 10 SDK
- Windows SDK tools `makeappx.exe` and `signtool.exe`
- A public code-signing certificate for direct distribution, or a reserved
  Microsoft Partner Center identity for Store distribution

Set the identity values to the exact values assigned by Partner Center or the
signing certificate:

```powershell
$env:FTITC_WINDOWS_PACKAGE_IDENTITY = "Your.Store.Package.Identity"
$env:FTITC_WINDOWS_PUBLISHER = "CN=Exact certificate subject"
$env:FTITC_WINDOWS_PUBLISHER_DISPLAY_NAME = "Publisher display name"
```

Prefer a certificate already installed in the Windows certificate store:

```powershell
$env:FTITC_WINDOWS_CERT_SHA1 = "CERTIFICATE_THUMBPRINT"
./AnalysisITC.Avalonia/Packaging/windows/package-windows.ps1 -Runtime win-x64
```

A PFX can be used locally, but neither it nor its password should be committed:

```powershell
$env:FTITC_WINDOWS_SIGNING_CERT = "C:\secure\ft-itc-signing.pfx"
$env:FTITC_WINDOWS_SIGNING_CERT_PASSWORD = "..."
./AnalysisITC.Avalonia/Packaging/windows/package-windows.ps1 -Runtime win-x64
```

For a Microsoft Store submission that will be signed by the Store, use
`-Unsigned` explicitly. The publisher must still exactly match the Partner
Center identity:

```powershell
./AnalysisITC.Avalonia/Packaging/windows/package-windows.ps1 -Runtime win-x64 -Unsigned
```

The script timestamps and verifies the primary executable and the locally
signed package. A self-signed
certificate is suitable only for development machines that explicitly trust
it, not for public distribution.

## Linux DEB

Requirements:

- A Debian-compatible Linux build host
- .NET 10 SDK
- `dpkg-deb`
- GnuPG and a release signing key

```bash
export FTITC_GPG_KEY_ID="full-key-fingerprint"
AnalysisITC.Avalonia/Packaging/package.sh linux --runtime linux-x64
```

The Linux packager performs a runtime-specific restore before publishing. If
`--no-restore` is used, the matching target (for example,
`net10.0/linux-x64`) must already exist in
`AnalysisITC.Avalonia/obj/project.assets.json`; a generic project or solution
restore is not sufficient for a self-contained runtime-specific publish.

The output includes the DEB, a SHA-256 checksum, and an ASCII-armored detached
signature. Linux does not have a universal executable code-signing trust model.
For a public package repository, sign the APT repository metadata as well; for
Flathub, the repository supplies the end-user trust and update channel.

Use `--unsigned` only for local installation tests.

## macOS DMG

Requirements:

- macOS with Xcode command-line tools
- .NET 10 SDK
- An Apple Developer ID Application certificate in Keychain
- An App Store Connect notarization profile for public distribution

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

## Unified entry point

The wrapper forwards platform-specific options:

```bash
AnalysisITC.Avalonia/Packaging/package.sh windows -Runtime win-x64
AnalysisITC.Avalonia/Packaging/package.sh linux --runtime linux-x64
AnalysisITC.Avalonia/Packaging/package.sh macos --runtime osx-arm64 --notarize
```

There is deliberately no `all` command because trustworthy release packages
should be built and tested on their native build hosts.

## Release checks

Before publishing:

1. Build from a clean, immutable release tag.
2. Confirm the project version, package version, About dialog, and tag agree.
3. Install on a clean target machine as a normal user.
4. Open an associated `.ftxtc` or `.ftitc` file from the operating-system file manager.
5. Verify open, edit, save, export, upgrade, and uninstall behavior.
6. Verify signatures independently after downloading the release artifact.
7. Publish checksums and retain signing/notarization logs without credentials.
