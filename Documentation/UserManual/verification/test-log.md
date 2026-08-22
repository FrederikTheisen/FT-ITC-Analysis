# Verification log - 2026-08-22

## Manual source

`validate_manual.py` passed:

- 12 ordered pages;
- 74 feature rows;
- all required front matter;
- all internal links and image references;
- captions and alt text;
- five unique external targets;
- no placeholders or framework-specific callout directives.

The repository, releases page, and wiki opened directly. The issue tracker and DOI were cross-checked through live links on the repository page after the page fetcher returned transient errors for those two direct destinations.

## Product tests

The native macOS build was launched with the repository's JORS example project. It reported version 1.4.3, loaded five project items including three experiments, selected the stored global one-set-of-sites Analysis Result, and completed its update and citation checks. The Avalonia build loaded the same project for the canonical workspace capture.

### Avalonia

`dotnet test AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj --no-restore --disable-build-servers`

- Passed: 57
- Failed: 0
- Skipped: 0

### Shared core

`dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --no-restore --disable-build-servers`

- Passed: 180
- Failed: 2
- Skipped: 0

Both failures are the existing `PublishedTwoSiteModelReproductionTests.TwoSetsOfSitesReproducesPublishedFeOtf54Parameters` cases. The fitted `dH1` differs from the expected value by 37.170% for Nelder-Mead and 36.238% for Levenberg-Marquardt. This manual implementation does not modify model code or the user's benchmark work. Matrix row F036 records the caveat. The user-facing chapter already cautions that two-site fits require identifiability, parameter stability, and scientific support.

The restore phase also reported that NuGet vulnerability metadata was unavailable; no dependency restore was requested and the tests used the existing lock/build inputs.

## PDF

The dedicated ReportLab renderer produced one A4 PDF. Poppler rendered all 41 pages to PNG. The montage and targeted full-page views were inspected for clipping, overlap, page breaks, table readability, glyph loss, headers, and footers. One weak content break was corrected before the final render by moving the canonical workspace screenshot to a deliberate figure plate.

`verify_pdf.py` verifies page geometry and count, metadata, text extraction, bookmarks, internal links, and external links. The final run output is recorded in the delivery summary.
