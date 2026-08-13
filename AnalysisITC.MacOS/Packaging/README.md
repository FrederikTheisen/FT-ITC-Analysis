# Xamarin.Mac release pipeline

This folder contains the complete release pipeline for the original universal
Xamarin.Mac application. It is independent of the cross-platform Avalonia
packager.

## One-command release

Clean, test, restore, publish, package, sign, notarize, staple, and verify:

```bash
AnalysisITC.MacOS/Packaging/package-macos.sh --notarize
```

The final DMG and its SHA-256 checksum are written to:

```text
AnalysisITC.MacOS/Packaging/output/
```

Temporary publish and DMG-staging files remain under
`AnalysisITC.MacOS/Packaging/work/`. Both generated directories are ignored by
Git.

`installer-background.png` supplies the branded Finder background. The package
stage also restores the original app and Applications icon positions.

## Individual stages

Each stage can also be run independently:

```bash
AnalysisITC.MacOS/Packaging/clean.sh
AnalysisITC.MacOS/Packaging/publish.sh
AnalysisITC.MacOS/Packaging/package.sh
AnalysisITC.MacOS/Packaging/sign.sh --notarize
```

Xamarin/MSBuild signs the application during the Release publish so that its
nested binaries and entitlements are handled by the native Xamarin.Mac build.
The final signing stage verifies that application signature, signs the DMG,
and optionally submits it for notarization.

## Requirements

- macOS and Xcode command-line tools;
- Xamarin.Mac with Mono `msbuild`;
- `create-dmg` for the branded Finder window and drag-to-Applications layout;
- .NET 10 SDK for the shared-core release tests;
- the Developer ID identity configured in `AnalysisITC.MacOS.csproj`; and
- valid App Store Connect notarization credentials for notarized releases.

## Options

The one-command entry point supports:

- `--notarize`: submit, staple, and Gatekeeper-check the signed DMG;
- `--unsigned`: create a local unsigned application and DMG;
- `--no-restore`: reuse the existing legacy NuGet package restore;
- `--skip-tests`: skip the shared-core release tests; and
- `--no-clean`: preserve the current release workspace before publishing.

`--unsigned` and `--notarize` cannot be combined.
