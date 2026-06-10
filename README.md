# FT-ITC Analysis
<p align="center">
 
![Dynamic XML Badge](https://img.shields.io/badge/dynamic/xml?url=https%3A%2F%2Fraw.githubusercontent.com%2FFrederikTheisen%2FFT-ITC-Analysis%2Frefs%2Fheads%2Fmaster%2FInfo.plist&query=plist%2Fdict%2Fkey%5B.%3D%22CFBundleVersion%22%5D%2Ffollowing-sibling%3A%3Astring%5B1%5D&label=version&color=green)
 [![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.14832177.svg)](https://doi.org/10.5281/zenodo.14832177) ![Static Badge](https://img.shields.io/badge/publication-pending-orange) ![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/FrederikTheisen/FT-ITC-Analysis/total?style=flat)

</p>

FT-ITC Analysis is a macOS desktop application for processing, analyzing, and presenting isothermal titration calorimetry (ITC) experiments. It is intended for working with ITC data away from the instrument workstation, with tools for baseline correction, peak integration, model fitting, multi-experiment analysis, and export of publication-style figures and processed data. 
 

This repository is open source and exists both to distribute end-user binaries through GitHub Releases and to provide a citable, versioned software record.

## Features

- Process raw thermograms with spline, polynomial, or segmented baseline correction.
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

## Basic Workflow

1. Open one or more supported data files using **File > Open...** or drag-and-drop.
2. Review the experiment metadata and correct concentrations, temperature, or attributes if needed.
3. Process each thermogram by fitting a baseline and choosing integration regions.
4. Fit a model to one experiment, or select multiple experiments for global analysis.
5. Review residuals, uncertainty estimates, and derived thermodynamic values.
6. Export figures, processed data, integrated heats, or fit parameters as needed.
7. Save the session as a `.ftitc` project file to preserve the full analysis state.

## Baseline Processing

FT-ITC Analysis supports three baseline modes:

- **Spline:** places baseline control points between injections and interpolates them with a linear or smooth spline. The sparse, balanced, and dense point-density settings control the target number of points per injection, while the app adapts the actual point count to the usable baseline length after each injection.
- **Polynomial:** fits an outlier-discarding polynomial baseline across the thermogram.
- **Segmented:** fits local baseline segments around injection regions.

By default, points inside the selected integration regions are excluded from baseline fitting so peak area selection and baseline estimation stay coupled. Changing integration ranges can therefore change the fitted baseline unless the processor or relevant spline points are locked.

Spline baselines can be edited directly. User-added or locked spline points are preserved when the baseline is reprocessed, and locked points replace nearby automatically generated points instead of being duplicated. Polynomial and segmented baselines can also be converted to spline baselines when manual editing is needed.

## Help and Support

The application includes built-in help and science notes covering data loading, processing, fitting, uncertainty estimation, and figure export.

If you report a bug, please include:

- the application version,
- a minimal example dataset or exported project when possible.

The app can generate a support report from the Help menu. Review the report before sending it if your log or data file names may contain sensitive information.

## Privacy and Network Access

FT-ITC Analysis processes data locally. It does not upload experiment data.

The app may contact this GitHub repository on launch to check for version information and citation metadata. This can be disabled in preferences. If those checks fail or are disabled, the application continues to work with local/default metadata.

## Citation

Citation information is available inside the program through **Help > Citation**.

## Development

The project is a Xamarin.Mac / Visual Studio for Mac / XCode application.

## License

MIT License. See `LICENSE.md`.
