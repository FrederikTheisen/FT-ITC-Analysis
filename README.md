# FT-ITC Analysis

FT-ITC Analysis is a macOS desktop application for processing and analyzing isothermal titration calorimetry (ITC) experiments. The program allows users to process and analyze data obtained from MicroCal instruments on their own computers. The implementation improves the baseline adjustment options and allows fast changes of peak integration ranges. The program implements global analysis options using standard models with parameters that can be fixed or temperature dependent. Multi epxeriment analyses allows further analysis using various tools for determination of structuring, counterio release and protonation state changes.

This repository is open source and exists both to distribute end-user binaries (GitHub Releases) and to provide a citable, versioned software record.

## Supported data files

- **MicroCal raw data**: `.itc`
- **TA Instruments / NanoAnalyze export**: `.TA`

## Installation

1. Download the latest `.dmg` from GitHub Releases.
2. Open the DMG and drag `FT-ITC-Analysis.app` to `/Applications`.
3. Launch the application.
    - First time launch will require giving permission to run the app.

Releases are intended to be **Developer ID–signed and notarized** to reduce Gatekeeper warnings for apps distributed outside the Mac App Store.

## Basic workflow

1. Open one or more supported data files (`.itc`, `.TA`) (menu or drag-and-drop).
    - Apply concentration corrections and add buffer/salt options if relevant.
2. Perform baseline fitting and peak integration.
3. Review processed injection heats and perform standard model fitting.
4. Grouped analysis of multiple experiments to for global fitting or derived parameters.
5. Export figures and/or processed results as needed.

## Citation

This repository contains (or should contain) a `CITATION.cff` file to tell others how to cite the software.

TODO Create a GitHub Release for each public version.

## Contributing

Issues and pull requests are welcome.

If reporting bugs, include:
- the input file type (`.itc` or `.TA`),
- what you expected vs what you observed,
- (if possible) a minimal example dataset or export.

## License

MIT License (see `LICENSE`).
