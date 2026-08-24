# FT-ITC Analysis

[![Latest release](https://img.shields.io/github/v/release/FrederikTheisen/FT-ITC-Analysis?display_name=tag&sort=semver&label=release)](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/FrederikTheisen/FT-ITC-Analysis/total)](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases)
[![DOI](https://img.shields.io/badge/DOI-10.5281%2Fzenodo.14832177-blue?logo=zenodo)](https://doi.org/10.5281/zenodo.14832177)
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

Packages for macOS, Linux, and Windows are published together with the same
version and release notes.

## Install

### macOS

1. Download the macOS DMG from the [latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest).
2. Open it and drag **FT-ITC.app** to **Applications**.
3. Launch **FT-ITC** from Applications.

The macOS application is signed and notarized.

### Linux

1. Download the Debian package for your architecture from
   the [latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest).
2. Install it with `sudo apt install ./ft-itc-analysis_<version>_amd64.deb`.
3. Launch **FT-ITC Analysis** from the application menu.

### Windows

1. Download the x64 setup executable from the
   [latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest).
2. Run the installer; administrator access is not required.
3. Launch **FT-ITC Analysis** from the Start menu.

The Windows installer supports x64 Windows 10 and Windows 11. It is currently
unsigned, so Windows may show **Unknown publisher** or a Microsoft Defender
SmartScreen warning. Only continue when the installer was downloaded from this
repository.

### Updates and removal

Install a newer package over the existing application to update it. Removing
the application does not delete projects, exported data, settings, or
autosaves. Report installation problems through the
[GitHub issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues).

## Supported files

- MicroCal-style raw data: `.itc`
- TA Instruments NanoITC native data: `.nitc`
- TA Instruments / NanoAnalyze exports: `.TA`
- PEAQ-ITC projects: `.apj`
- Legacy Origin ITC projects: `.opj` (first compatible worksheet)
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
