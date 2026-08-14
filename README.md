# FT-ITC Analysis

[![Latest release](https://img.shields.io/github/v/release/FrederikTheisen/FT-ITC-Analysis?display_name=tag&sort=semver&label=release)](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/FrederikTheisen/FT-ITC-Analysis/total)](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases)
[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.14832177.svg)](https://doi.org/10.5281/zenodo.14832177)
[![Website status](https://img.shields.io/website?url=https%3A%2F%2Fft-itc.org&label=website)](https://ft-itc.org)
[![Web viewer status](https://img.shields.io/website?url=https%3A%2F%2Fapp.ft-itc.org&label=web%20viewer)](https://app.ft-itc.org)

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

Platform packages are published together in a single GitHub release so that
macOS, Linux, and Windows downloads share the same version and release notes.
The macOS build is the stable release. The Linux and Windows builds are
currently pre-release packages. The pre-release packages are the recommended versions.

## Install

### macOS — available

1. Download the DMG from the [latest GitHub release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest).
2. Open it and drag **FT-ITC.app** to **Applications**.
3. Launch the app from Applications or open a supported data/project file.

The public macOS DMG is signed and notarized. Removing the app does not remove projects, exported data, settings, or autosaves.

### Linux — pre-release

Download the package for your architecture from the
[latest GitHub release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest).
Linux packages are provided as Debian packages and should currently be treated
as pre-release builds. Please report platform-specific issues through the
[GitHub issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues).

### Windows — pre-release

1. Open the [GitHub releases page](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases).
2. Download `ft-itc-analysis-<version>-win-x64-setup.exe` from the newest release.
3. Run the installer and launch **FT-ITC Analysis** from the Start menu.

The Windows package is intended for x64 Windows 10 and Windows 11 and installs
for the current user without administrator access. The prerelease installer is
currently unsigned, so Windows may show **Unknown publisher** or a Microsoft
Defender SmartScreen warning. Only bypass that warning for an installer
downloaded from this repository. 

Windows updates are currently manual: download a later installer and run it
over the existing installation. Please report Windows-specific issues through
the [GitHub issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues).

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

The optional web viewer uploads the selected file to its server for transient processing.
It does not retain the parsed document as application state and no information is logged.

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

Run these tests locally before packaging.

The original Xamarin.Mac packaging instructions are in
[AnalysisITC.MacOS/Packaging/README.md](AnalysisITC.MacOS/Packaging/README.md).

## Citation and license

Citation information is available through **Help > Citation** and will be
updated with the meta-paper record when it is published. The software archive
DOI is [10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177).

FT-ITC Analysis is distributed under the MIT License. Third-party notices are
included in [LICENSE.md](LICENSE.md).
