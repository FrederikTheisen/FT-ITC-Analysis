# Issue tracker

## ITC-001 — Asymmetric rounding of negative display values

- Priority: High
- Status: Open; implementation deferred.
- Location: `AnalysisITC.Core/Math/FWEMath.cs`, both `RoundApproximate` overloads.
- Problem: −2.5 rounds to −2 while +2.5 rounds to +3. The digits overload also rounds −2.6 to −2. Standard/Strict uncertainty formatting uses the one-argument overload, so the midpoint inconsistency reaches displayed values and interval endpoints.
- Follow-up: Make midpoint tolerance and rounding symmetric for both signs, preserve the requested midpoint mode, and add independent positive/negative reference cases for both overloads and formatted output.

## ITC-002 — Expanded bounds on logarithmic affinity coordinates

- Priority: Medium
- Status: Open; do not change bounds until the intended policy is agreed.
- Location: `AnalysisITC.Core/Analysis2/ParameterSet.cs`, `Parameter.RefreshLimits`.
- Problem: Affinity is stored as log₁₀K. Multiplying the standard bounds `[-2, 20]` gives `[-40, 400]` for Extended and `[-4000, 40000]` for No limit. Exponentiating the upper limits overflows; the No limit lower bound underflows. A one-site model can still predict finite heat at log₁₀K = 400 while reporting K = ∞ and ΔG = −∞.
- Decision needed: Clarify whether negative log-affinity bounds are permitted. A negative log₁₀K represents a positive K below 1 in the application's affinity units; it is not a negative equilibrium constant. Also decide what physical widening each preset should represent before choosing additive log-space bounds and checking transformed global coordinates.

## ITC-003 — pKa correction can fail to converge

- Priority: Medium
- Status: Partially mitigated; convergence handling deferred.
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs`, `BufferAttribute.optpKa`.
- Problem: Fixed-point iteration can enter a repeating cycle. The actual phosphate calculation hangs at pH 8.198, 25 °C, and concentration 5 M without a cap. This is an extreme-concentration reproduction; the tested registry grid through 1 M converged.
- Current mitigation: Stop after 1,000 iterations and return the last estimate. There is deliberately no new failure, warning, or convergence-status handling.
- Follow-up: Decide how a non-converged ionic-strength correction should be represented and whether the iteration should be replaced by a more robust calculation.

## ITC-004 — Unlocking a spline point discards a converted spline

- Priority: Medium
- Status: Open; accepted consequence of protecting converted splines with the processing lock.
- Location: Spline point context menu "Unlock" in `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs` and `AnalysisITC.MacOS/GraphViews/DataProcessingGraphView.cs`.
- Problem: "Unlock" clears the point's position and slope locks and then calls `ProcessData()` with `replace: true`, which regenerates every spline point from the raw data. On a spline converted from a Polynomial or Segmented baseline, unlocking one point therefore discards the whole conversion rather than releasing that point. Converted Smooth-spline points carry locked slopes, so this is also the only way to release a converted slope.
- Follow-up: Decide what unlocking a single point should do on an existing spline (for example, release the point and refresh from the current points with `replace: false`) and keep both applications aligned.

## ITC-005 — Standardization of inspector headers

- Priority: Low
- Status: Completed.
- Location: Inspector section headings and tabs across the macOS and Avalonia applications.
- Problem: Heading capitalization was inconsistent, with sentence case and title case mixed across inspectors and tools.
- Resolution: Standardized the affected inspector headings and related tabs to sentence case, preserving proper names and acronyms.

## ITC-006 — Report test depends on the local time zone

- Priority: High
- Status: Open; found in the October 2026 Analysis Report audit.
- Location: `AnalysisITC.Core.Tests/AnalysisReportEnhancementTests.cs`, `BuilderUsesExplicitUnicodeAuthorAndFreshGenerationMetadataAndLimitsSignaturesToCover` (expected string near line 225).
- Problem: The test expects `"3 Sep 2026 13:15 UTC+03:00"` for a generation time of 10:15 UTC, so it only passes on a machine set to UTC+03:00. It fails in UTC containers, in CI and for contributors in other zones. `AnalysisReportDocument.GeneratedAtUtc` formats `ExportDateText` with `TimeZoneInfo.Local`.
- Suggested fix: Compute the expected text in the test from `TimeZoneInfo.Local` (convert `generated`, format with `"d MMM yyyy HH:mm"` and the local offset), or give `AnalysisReportDocument` an internal time-zone hook defaulting to `TimeZoneInfo.Local` and set a fixed custom +03:00 zone in the test. Keep one assertion that the text includes the offset.

## ITC-007 — Multi-result reports label every result "1" in Result identifiers

- Priority: High
- Status: Open; reproduced with a two-result report and Extra traceability enabled.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`, `CreateDocument` (`document.AddResult(ResultReference(result, 0))`) and `BuildAppendix` ("Result identifiers").
- Problem: Each result chapter is built as a single-result child document whose only result reference always has index 0. Result 2's appendix therefore reads `1: <ID of result 2>`. This is a wrong traceability record in the mode intended for traceability.
- Suggested fix: Pass `resultIndex` from `BuildSingleResult` into `CreateDocument` and use it in `ResultReference`. Consider also emitting a single report-level "Report details" block for multi-result reports instead of repeating it in every chapter. Add a test asserting that chapter N lists `N: <ID of result N>`.

## ITC-008 — Long notice blocks are silently clipped

- Priority: High
- Status: Open; reproduced in layout (a 120-line notice kept about 59 drawable lines).
- Location: `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs`, `PlaceNotice` and the height clamp in `Add`.
- Problem: Notices are never split across pages. When a notice is taller than the remaining space, `Add` clamps the fragment to the page and both renderers stop drawing at the fragment bottom. Lines are lost without any indication. This affects "Report warnings", validity notices and interpretation provenance.
- Suggested fix: Split notices like `PlaceText`: keep them together when they fit on a page, otherwise emit consecutive fragments with `first`/`count` line ranges. Draw the title on the first fragment only, or as "<title> (continued)". Size each fragment as title + lines × line height + padding. Add a layout test asserting that all lines are placed and that each fragment's lines fit its bounds.

## ITC-009 — Single-page tables can shrink until illegible

- Priority: High
- Status: Open; reproduced in layout (a 70-row overview table placed late on a page was drawn at scale 0.08, about 0.6 pt type).
- Location: `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs`, `PlaceTable`, `ShrinkToSinglePage` branch.
- Problem: A new page is started only when the full table fits on an empty page. A table taller than a page is shrunk into whatever space remains on the current page, with no lower bound on legibility. The experiment parameter overview uses this policy.
- Suggested fix:
  1. If the table does not fit in the remaining space and the current page already holds content beyond the section title, start a new page before shrinking.
  2. Introduce a minimum scale (for example 0.65, about 5 pt for 7.5 pt tables).
  3. If the table still needs a smaller scale on an empty page, ignore `ShrinkToSinglePage` and split it with repeated headers.
  4. Add tests for both the late-on-page and the oversized cases.

## ITC-010 — Cover experiment overview can collapse to a sliver

- Priority: Medium
- Status: Open; reproduced in layout (canvas heights of 22 pt and 5 pt depending on comment length).
- Location: `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs`, `PlaceFigureCanvas` (cover branch).
- Problem: On the cover, the figure canvas takes `Math.Min(Remaining, UsableHeight() * .50)` with no minimum. The 100 pt minimum used on other pages is skipped for the cover. Long result comments or validity reasons push the canvas into a few points of space. The cover can also spill onto a second page, because section-level `ShrinkToSinglePage` is never applied.
- Suggested fix: Apply a minimum canvas height on the cover as well (for example 30% of usable height) and start a new page when it is not available. Optionally move long result comments and validity details out of the cover into the analysis summary. Either implement section-level `ShrinkToSinglePage` or remove the flag from section definitions so the policy does not imply a guarantee.

## ITC-011 — Fixed shared parameters are listed repeatedly

- Priority: Medium
- Status: Open; reproduced (a locked SameForAll N appeared as "N-value — Experiment 1", "N-value — Experiment 2" and "Shared N-value").
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`, `BuildFixedParameterItems`.
- Problem: `ParameterFitStatus` returns "Fixed" for every member when a shared global parameter is locked, so each member gets its own item. The global loop then adds a "Shared" item as well.
- Suggested fix: In the per-member loop, skip parameters whose fixed status comes from a global constraint (constraint not `None` and the shared or coordinate keys are locked). Leave those to the "Shared" loop. Add a test expecting exactly one "Shared N-value" entry.

## ITC-012 — Exported PDF can carry the preview's generation time

- Priority: Medium
- Status: Open; behavior contradicts the manual ("Export rebuilds the report with the current user name and generation time").
- Location: `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs`, `ExportAsync` / `EnsureExportDocumentAsync`; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs`, `ExportAsync`.
- Problem: Export reuses the preview document whenever it is not stale. "Generated at", the header date and the author therefore reflect the preview, which may be hours old or from before the user name changed.
- Suggested fix: Always rebuild with fresh generation metadata at export (call `BuildAsync(showPreview: false)` unconditionally, keeping the preview visible). If rebuild cost is a concern, rebuild only the document and layout, since pagination does not depend on the timestamp. Otherwise, change the manual to say export uses the previewed document.

## ITC-013 — Advanced-analysis plots ignore the selected uncertainty style

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`, `BuildAffinitySaltPlot`, `BuildDebyeHuckelPlot`, `BuildCounterIonReleasePlot`, `BuildProtonationPlot`; whisker drawing in `SkiaAnalysisReportRenderer.DrawPlot` and the macOS equivalent.
- Problem: These plots are created with the default `UncertaintyDisplayStyle.Automatic` and points built by `PlotPoint`, which sets `Lower`/`Upper` to the 95% CI only. The renderer's Automatic/SD branch falls back to `point.Lower`/`point.Upper`, so CIs are drawn with SD-style caps and no legend. "None" still draws whiskers. The manual states that the selected style controls applicable analysis plots.
- Suggested fix:
  1. Pass `options.UncertaintyDisplayStyle` to these plot blocks.
  2. Build points with `PlotPointForDisplay`, so SD and CI bounds are populated separately.
  3. For log-transformed axes, where SD is not meaningful, populate the CI only and treat SD requests as CI.
  4. In both renderers, draw SD whiskers only from the `StandardDeviation*` fields; remove the `?? point.Lower` fallback.
  5. Add a short caption stating what the whiskers show.

## ITC-014 — Thermodynamic summary colors repeat after five series

- Priority: Low
- Status: Open.
- Location: `SeriesColor` in `AnalysisITC.Avalonia/Drawing/SkiaAnalysisReportRenderer.cs` and `AnalysisITC.MacOS/Drawing/CoreGraphicsAnalysisReportRenderer.cs`.
- Problem: The palette cycles every five entries, so results with six or more members show identical swatches for different experiments (for example 1A and 1F).
- Suggested fix: Move the palette to Core and share it between platforms. Extend it to at least eight distinguishable, print-safe colors, then vary a secondary channel such as hatching or outline style once colors repeat. Alternatively, label bars with their experiment reference when series exceed the palette.

## ITC-015 — Correlation notes are truncated and unwrapped

- Priority: Low
- Status: Open.
- Location: `DrawCorrelation` in both report renderers (`matrix.Notes.Take(3)`); `PlaceCorrelation` in `AnalysisReportLayout.cs`.
- Problem: Only the first three notes are drawn, each on one unwrapped line, so reliability warnings can be dropped or run past the margin. The layout reserves a fixed 68 pt for title and notes, so three notes already slightly exceed it.
- Suggested fix: Keep only the replicate summary inside the matrix block. Have `AddCorrelation` emit the remaining notes as a following `AnalysisReportTextBlock` or notice, so the layout wraps and paginates them normally.

## ITC-016 — Long section titles overlap the status badge

- Priority: Low
- Status: Open.
- Location: `PlaceSectionTitle` in `AnalysisReportLayout.cs`; status-badge drawing in both renderers.
- Problem: Section titles wrap across the full content width, while the badge is drawn at the right edge of the same rectangle. Long result names ("Result 2. …") run underneath the badge.
- Suggested fix: When a section shows a badge (cover, or `StatusBadgeHealth` set), wrap the title at the content width minus a reserved badge width (for example 110 pt). Optionally measure the badge text in the layout for an exact reserve.

## ITC-017 — Inline Markdown emphasis inverts after a page break

- Priority: Low
- Status: Open.
- Location: `PlaceText` in `AnalysisReportLayout.cs`; `DrawInlineMarkdownLines` in both renderers.
- Problem: Renderers carry bold and italic state across lines within a fragment but reset it at the start of each fragment. A `**…**` span split by a page break renders as regular text after the break, and the closing marker then turns bold on for the rest of the paragraph. A stray single `*` (for example a footnote marker) toggles italic for the rest of the block.
- Suggested fix: Record the open emphasis state at each fragment start (for example `StartsBold`/`StartsItalic` on `AnalysisReportLayoutFragment`, computed while splitting) and initialize the renderers from it. Treat `*` as a marker only when it is adjacent to a non-space character on the inner side, matching CommonMark flanking rules.

## ITC-018 — Key-value layout and rendering use different text metrics

- Priority: Low
- Status: Open.
- Location: `KeyValueRowHeight` in `AnalysisReportLayout.cs`; `DrawKeyValues` in both renderers.
- Problem: The layout wraps labels at `30% − 3 pt − indent` with regular weight. The renderers re-wrap at `30% − 6 pt − indent` and draw the label bold, and use a fixed 12 pt line advance. Long labels can wrap differently from the plan or overflow into the value column.
- Suggested fix: Precompute wrapped label and value lines and row heights in the layout and store them on the fragment, as `AnalysisReportTableLayout` already does for tables. The renderers then only draw. Measure labels with the bold style. Share the column fraction and padding constants.

## ITC-019 — Molar RMSD ignores the report energy unit

- Priority: Low
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`, `BuildFitDiagnosticItems` and `BuildMemberFitItems`.
- Problem: Molar RMSD is always formatted in kJ/mol, even when the report is set to calories.
- Suggested fix: Format with `ResolveMolarEnergyUnit(result, options)`, as the other molar energies in the report do.

## ITC-020 — Saved report definitions accumulate and are never removed

- Priority: Medium
- Status: Open; decision needed on intended lifecycle.
- Location:
  - `AnalysisITC.Core/DataManager.cs`: `RemoveReport` has no callers; `Init`/`Clear` empty reports, and the clear undo log does not include them.
  - `EnsureReportRegistered` and `MarkStale` in `AnalysisReportWindow.cs` and `AnalysisReportViewController.cs`.
  - `ProjectWriter.SaveSelectedAsync`.
- Problem:
  - Each distinct ordered result and supporting-experiment selection becomes a persisted report as soon as a presentation setting changes or a supporting experiment is selected.
  - There is no UI to list or delete these reports.
  - Reports that reference deleted results remain in the project file.
  - Undoing a project clear restores data but not reports.
  - Saving a single result drops its report and approved interpretation.
- Suggested fix:
  1. Register a report only when it holds user content worth keeping (an interpretation, study context, or a non-default presentation set explicitly). Keep a draft in memory otherwise.
  2. On save, either drop reports whose result IDs no longer all resolve, or keep them but surface them as orphaned.
  3. Include reports in the clear/delete undo log.
  4. When saving a single result, include reports whose result and supporting IDs are covered by the saved content.
  5. Consider a small "Saved reports" list in the report window with delete.

## ITC-021 — Interpretation editor does heavy work on every keystroke

- Priority: Low
- Status: Open; not measured.
- Location: `interpretationBox` text-change handler and `MarkStale`/`UpdateInterpretationStatus` in `AnalysisReportWindow.cs`; the macOS equivalent.
- Problem: Each keystroke runs `MarkStale`, which builds `AnalysisReportOptions` twice, compares presentation settings and, for automated interpretations, re-runs `AnalysisInterpretationService.EvaluateFreshness` across all selected results and experiments. This may lag with large selections.
- Suggested fix: Debounce the editor's stale marking (for example 300 ms). Re-evaluate freshness only on source-data change events, selection changes or editor commit, and cache the last result.

## ITC-022 — Report build mutates the shared report off the UI thread

- Priority: Medium
- Status: Open.
- Location: `AnalysisReportBuilder.Build(AnalysisReport, …)` (`report.SetInterpretationFreshness(...)`); called inside `Task.Run` from both report windows. `ResolveReferenceName` falls back to `DataManager.Data` when no resolver is supplied.
- Problem: The builder writes freshness state onto the live `AnalysisReport` from a thread-pool thread while the UI thread may read it in `UpdateInterpretationStatus`. Builds also read live result and experiment objects that other windows can modify concurrently.
- Suggested fix: Make the builder side-effect free. Build from `report.CreateDetachedCopy()`, return the evaluated freshness on the document, and apply it to the live report on the UI thread after the build completes. Always pass an explicit experiment resolver so the `DataManager` fallback is not used off the UI thread.

## ITC-023 — Interpretation privacy wording differs between app and manual

- Priority: Medium
- Status: Open; wording decision needed.
- Location: privacy hint in `AnalysisInterpretationDialog` (`AnalysisReportWindow.cs`) and `PrivacyNotice()` (`AnalysisReportViewController.cs`); `Documentation/UserManual/pages/09-figures-printing-export.md`, "Online generation privacy".
- Problem: Both dialogs state that "Abuse-monitoring retention may last up to 30 days". The manual states that provider and infrastructure retention are separate and that no deletion deadline is guaranteed. Users get two different retention statements.
- Suggested fix: Confirm the current provider and infrastructure retention terms. Put the agreed sentence in one shared Core string used by both dialogs, and update the manual to match.

## ITC-024 — Minor report builder and layout cleanups

- Priority: Low
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`, `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs`.
- Problem and suggested fixes:
  - An empty report shows two equivalent errors ("Select at least one saved analysis result." and "The report does not reference any analysis results."). Skip `missing-report-results` when result validation already reports `missing-results`.
  - `AddSupportingFigures` builds the processing figure without `PlotHeightCentimeters = FinalFigureHeightCentimeters`, unlike result chapters. Extract a shared `ProcessingFigureOptions(options)` helper for both. Also fix the mis-indented `else if (heats != null)` branch.
  - `temperaturePlotAdded` in `BuildAdvancedSections` is dead, because requests are already de-duplicated by key. Remove it.
  - Experiment labels are derived from members filtered by `Data != null`, while `BuildThermodynamicSummaryPlot` and the multi-result label map index unfiltered or differently filtered lists. Derive labels from one member-to-label map to avoid mislabeling if a member lacks data.
  - Headings and section titles have no keep-with-next rule and can be orphaned at the bottom of a page. Ensure room for the heading plus the first line of the following block.
