# FT-ITC-Analysis

FT-ITC-Analysis is a macOS desktop application for processing and analyzing isothermal titration calorimetry (ITC) experiments.

This repository is open source and exists both to distribute end-user binaries (GitHub Releases) and to provide a citable, versioned software record.

## Downloads (recommended)

Download the latest notarized DMG from GitHub Releases and install by dragging the app to `/Applications`.

## Supported data files

- **MicroCal raw data**: `.itc`
- **TA Instruments / NanoAnalyze export**: `.TA`

## System requirements

- **CPU**: Intel (x86_64)
  - Apple Silicon Macs can run the Intel build using **Rosetta 2**.
- **macOS**: TODO (minimum supported version)

## Installation

1. Download the latest `.dmg` from GitHub Releases.
2. Open the DMG and drag `FT-ITC-Analysis.app` to `/Applications`.
3. Launch the app.

Releases are intended to be **Developer ID–signed and notarized** to reduce Gatekeeper warnings for apps distributed outside the Mac App Store.

## Basic workflow

1. Open one or more supported data files (`.itc`, `.TA`) (menu or drag-and-drop).
2. Inspect baseline/peaks and integration.
3. Review processed injection heats and derived results.
4. Export figures and/or processed results as needed.

## Scope and limitations

- macOS-only distribution.
- The supported user workflow is using the prebuilt Releases (DMG).
- Source is public for transparency and reuse, but building from source is not currently a supported end-user workflow.

## Citation

This repository contains (or should contain) a `CITATION.cff` file to tell others how to cite the software.

Recommended approach for a stable, citable reference:
1. Create a GitHub Release for each public version.
2. Enable Zenodo’s GitHub integration for this repository so each release is archived and assigned a DOI.
3. Update `CITATION.cff` with the DOI (and later, when you publish a short software note, add it as a preferred citation).

## Contributing

Issues and pull requests are welcome.

If reporting bugs, include:
- the input file type (`.itc` or `.TA`),
- what you expected vs what you observed,
- (if possible) a minimal example dataset or export.

## License

MIT License (see `LICENSE`).
