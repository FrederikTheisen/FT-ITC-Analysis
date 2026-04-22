# FT-ITC Analysis

FT-ITC Analysis is a macOS desktop application for processing, analyzing, and presenting isothermal titration calorimetry (ITC) experiments. It is intended for working with ITC data away from the instrument workstation, with tools for baseline correction, peak integration, model fitting, multi-experiment analysis, and export of publication-style figures and processed data.

This repository is open source and exists both to distribute end-user binaries through GitHub Releases and to provide a citable, versioned software record.

## Features

- Process raw thermograms with spline or polynomial baseline correction.
- Adjust integration regions globally or per injection.
- Fit standard ITC binding models, including one-set-of-sites, two-sets-of-sites, competitive binding, and dissociation models.
- Run multi-experiment and global analyses with shared, free, fixed, or temperature-dependent parameters where applicable.
- Estimate parameter uncertainty using resampling-based methods.
- Analyze temperature, salt, and buffer-dependent experiments for derived thermodynamic interpretation.
- Merge tandem titration experiments and perform buffer subtraction.
- Export integrated heats, processed data, fit results, and publication-oriented figures.
- Save portable `.ftitc` project files containing data, processing state, fit results, and analysis results.

## Supported Data Files

- **MicroCal-style raw data:** `.itc`
- **TA Instruments / NanoAnalyze exports:** `.TA`, `.ta`
- **PEAQ-ITC project files:** `.apj`
- **Integrated heats:** `.dat`, `.aff`, `.dh`
- **FT-ITC Analysis project files:** `.ftitc`

Raw input files are read by the application but are not modified. Save work as a `.ftitc` project file if you want to preserve processing settings, analysis results, and comments.

## Installation

1. Download the latest `.dmg` from GitHub Releases.
2. Open the DMG and drag `FT-ITC-Analysis.app` to `/Applications`.
3. Launch the application.

For distribution outside the Mac App Store, releases should be Developer ID signed and notarized. If macOS blocks first launch for an unsigned or locally built copy, use Finder to open the app explicitly and confirm that you want to run it.

## Basic Workflow

1. Open one or more supported data files using **File > Open...** or drag-and-drop.
2. Review the experiment metadata and correct concentrations, temperature, or attributes if needed.
3. Process each thermogram by fitting a baseline and choosing integration regions.
4. Fit a model to one experiment, or select multiple experiments for global analysis.
5. Review residuals, uncertainty estimates, and derived thermodynamic values.
6. Export figures, processed data, integrated heats, or fit parameters as needed.
7. Save the session as a `.ftitc` project file to preserve the full analysis state.

## Help and Support

The application includes built-in help and science notes covering data loading, processing, fitting, uncertainty estimation, and figure export.

If you report a bug, please include:

- the application version,
- the macOS version,
- the input file type,
- what you expected to happen,
- what happened instead,
- steps to reproduce the issue,
- a minimal example dataset or exported project when possible.

The app can generate a support report from the Help menu. Review the report before sending it if your log or data file names may contain sensitive information.

## Privacy and Network Access

FT-ITC Analysis processes data locally. It does not upload experiment data.

The app may contact this GitHub repository on launch to check for version information and citation metadata. This can be disabled in preferences. If those checks fail or are disabled, the application continues to work with local/default metadata.

## Citation

Citation information is available inside the program through **Help > Citation**. The repository also includes `CITATION.cff` and `citation.json` for software citation metadata.

## Development

The project is a Xamarin.Mac / Mono application.

Typical local build command:

```sh
msbuild AnalysisITC.sln /p:Configuration=Release
```

Before publishing a release, verify that:

- the app builds in Release configuration,
- version numbers match across `Info.plist`, `VERSION`, and citation metadata,
- the app is signed, notarized, and stapled if distributed as a public macOS binary,
- example files load correctly,
- save/load round trips preserve processing and analysis results,
- export workflows produce the expected files.

## License

MIT License. See `LICENSE.md`.
