# Xamarin.Mac packaging

This packages the original universal Xamarin.Mac application. It is separate
from the cross-platform Avalonia packager.

Requirements:

- macOS and Xcode command-line tools;
- Xamarin.Mac with Mono `msbuild`;
- .NET 10 SDK for the shared-core release tests; and
- the Developer ID identity configured in `AnalysisITC.MacOS.csproj`.

Create a signed DMG after running the core test suite:

```bash
AnalysisITC.MacOS/Packaging/package-macos.sh
```

For a local unsigned bundle/DMG test:

```bash
AnalysisITC.MacOS/Packaging/package-macos.sh --unsigned
```

Pass `--no-restore` only when the legacy `packages/` directory has already
been restored.

To notarize, configure the same Keychain profile used by the Avalonia macOS
packager and add `--notarize`:

```bash
export FTITC_MAC_NOTARY_PROFILE="FTITC-notary"
AnalysisITC.MacOS/Packaging/package-macos.sh --notarize
```

Artifacts and SHA-256 checksums are written below `artifacts/packages/`.
