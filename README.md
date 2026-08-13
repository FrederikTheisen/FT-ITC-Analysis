# FT-ITC Analysis

[Website](https://ft-itc.org) ·
[Latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest) ·
[All releases](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases) ·
[Software DOI](https://doi.org/10.5281/zenodo.14832177) ·
[License](LICENSE.md)

FT-ITC Analysis is an open-source desktop application for processing,
analyzing, and presenting isothermal titration calorimetry (ITC) data. It
supports baseline correction, peak integration, model fitting,
multi-experiment analysis, uncertainty estimation, and publication-oriented
figure and data export.

The current public desktop release is for macOS. Cross-platform Windows,
Linux, and macOS development is taking place in the Avalonia application in
this repository; those builds should be treated as pre-release until matching
packages are published on the Releases page.

## Install

### macOS release

1. Download the DMG from the [latest GitHub release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest).
2. Open it and drag **FT-ITC.app** to **Applications**.
3. Launch the app from Applications or open a supported data/project file.

The public macOS DMG is signed and notarized. Replacing the app during an
update does not remove projects, exported data, settings, or autosaves.

### Windows and Linux

Packaging support is under active development. Installation instructions will
be added when signed/tested public packages are available. Developer packaging
instructions are in
[AnalysisITC.Avalonia/Packaging/README.md](AnalysisITC.Avalonia/Packaging/README.md).

## Supported files

- MicroCal-style raw data: `.itc`
- TA Instruments / NanoAnalyze exports: `.TA`
- PEAQ-ITC projects: `.apj`
- Integrated heats: `.dat`, `.aff`, `.dh`
- FT-ITC Analysis projects: `.ftxtc` (current), `.ftitc` (legacy import)

Input data are read but not modified. Save work as `.ftxtc` to preserve data,
processing settings, fit results, analysis results, and comments.

## Main capabilities

- Spline, polynomial, and segmented baseline correction
- Global or per-injection integration-region editing
- One-set-of-sites, two-sets-of-sites, competitive, and dissociation models
- Multi-experiment/global analysis with shared, free, fixed, or
  temperature-dependent parameters
- Resampling-based uncertainty estimates
- Temperature-, salt-, and buffer-dependent analyses
- Tandem-experiment merging and buffer subtraction
- Publication-oriented figures and processed-data export

The application includes its own workflow help and scientific notes. More
documentation and project information are available at
[ft-itc.org](https://ft-itc.org).

## Privacy

The desktop applications process experiment data locally and do not upload it.
They may check this GitHub repository for version and citation metadata; this
can be disabled in preferences.

The optional web viewer uploads a selected file to its server for the duration
of the request. It does not intentionally retain the parsed document as
application state, although archive validation can use temporary server
storage. Deployments should use HTTPS and suitable log and temporary-file
retention policies.

## Development and tests

The repository contains a shared scientific core, the original Xamarin.Mac
application, the cross-platform Avalonia application, and a web viewer. The
.NET projects use the SDK selected by `global.json`.

Run the automated suites with:

```bash
dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --configuration Release
dotnet test AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj --configuration Release
dotnet test AnalysisITC.Web.Tests/AnalysisITC.Web.Tests.csproj --configuration Release
```

GitHub Actions runs these commands for pushes to `master` and pull requests.
Platform packages must additionally be built and smoke-tested on their target
operating systems.

Remove ignored build and packaging output without touching source files,
dependency caches, signing material, or project data with:

```bash
scripts/clean-generated.sh
```

The original Xamarin.Mac packaging instructions are in
[AnalysisITC.MacOS/Packaging/README.md](AnalysisITC.MacOS/Packaging/README.md).

## Citation and license

Citation information is available through **Help > Citation** and will be
updated with the meta-paper record when it is published. The software archive
DOI is [10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177).

FT-ITC Analysis is distributed under the MIT License. Third-party notices are
included in [LICENSE.md](LICENSE.md).
