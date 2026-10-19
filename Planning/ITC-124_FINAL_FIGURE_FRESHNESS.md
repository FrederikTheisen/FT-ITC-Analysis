# Final Figure freshness marking — potential future feature

Status: Potential future feature, registered 18 October 2026. Investigation and planning record only; implementation is not approved or scheduled. Tracker: [ITC-124](ISSUES.md#itc-124---mark-final-figures-when-the-displayed-fit-no-longer-matches-the-data).

## Requested behavior

When a Final Figure uses a solution that no longer matches the experiment data, mark the exported figure in two ways:

- A visible notice explaining that the fit and data do not match.
- A second, preferably invisible record that may remain when the visible notice is deleted during subsequent editing.

The request is to preserve this as a possible feature and investigate comparable software. Warning wording, appearance, detection rules, export eligibility and the hidden-record format remain open.

The user confirmed the preferred survival order on 18 October 2026:

1. Vector editing, such as opening the PDF in Illustrator, Affinity Designer or Inkscape, deleting the visible notice, editing the figure and saving/exporting it again.
2. Subsequent placement into PowerPoint, Word or a manuscript, including the resulting document export.

These are the priority workflows for future evaluation, not verified capabilities. The particular editors, document applications, versions and insertion/export methods remain to be selected. Metadata or a page note must be tested through the editing stage before being treated as sufficient for this request.

## Current FT-ITC evidence

The shared [figure builder](../AnalysisITC.Core/Presentation/PublicationFigure.cs), especially `ResolveModel`, `SourceSolution` and `Build`, resolves the selected solution and checks ownership and identities. It does not add a freshness warning when the selected solution is invalid or its saved inputs differ from current data. Both PDF writers already write document metadata: [Avalonia](../AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs), `WritePdf`, and [macOS](../AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs), `CreateFigurePdfData` / `BuildPdfMetadataKeywords`. Their existing fields describe the experiment and fit but do not record freshness.

Saved Analysis Results have an input-comparison mechanism in [AnalysisResultValiditySnapshot](../AnalysisITC.Core/DataClasses/AnalysisResultValiditySnapshot.cs). Standalone solutions expose `SolutionInterface.IsValid`; that flag alone does not establish that all fit inputs still match. Also, `AnalysisResult.ValidityReport` includes reasons other than changed input, such as uncertainty that was skipped under a previous assessment. A future data-mismatch warning should not simply rename every non-valid result as changed data.

A temporary Core probe on 18 October 2026 used a constructed One-Set-Of-Sites solution attached to an experiment with five integrated injections, wrapped it in an Analysis Result to capture fit inputs, and built Binding figures with optional details hidden. It then assigned a different measured temperature and changed an integrated heat followed by `UpdateProcessing()`:

| State | Solution flag | Saved-result input comparison | Figure builder output |
|---|---|---|---|
| Original inputs | Valid | Valid | One fit series; no freshness notice or metadata |
| Temperature changed from 25 to 35 °C | Still valid | Invalid: temperature changed | Fit retained; no freshness notice or metadata |
| Temperature and integrated heat changed; processing update published | Invalid | Invalid: temperature and processed heat changed | Fit retained; no freshness notice or metadata |

The changed-input cases behaved the same with an explicit result owner and with only the attached solution. This establishes the Core builder behavior for a constructed fixture, not an interactive desktop reproduction or a test of fitted numerical accuracy. Probe source and log were written to `/private/tmp/ftitc-figure-freshness-probe/`; the table records the evidence without relying on those temporary files remaining available.

The associated-result export paths currently differ. macOS `ExportAssociatedResult` checks `result.ValidityReport` and blocks non-valid results. Avalonia `ExportResultFiguresAsync` / `ExportResultFiguresToFolder` have no equivalent validity check; its menu requires member solutions. These are source observations, not a new approved change to either export policy.

## Comparable software

Primary documentation was checked on 18 October 2026. These are documented behaviors, not hands-on tests of the products. Application indicators, exported annotations and hidden provenance are separate mechanisms.

| Software | Documented behavior | Relevance and limit |
|---|---|---|
| Origin / OriginPro | Result sheets and graph windows show green locks for current results, yellow when changed inputs require recalculation, and red when an update cannot be performed. Manual and automatic recalculation are separate modes. [Freshness indicators](https://docs.originlab.com/quick-help/recaculation-status/), [recalculation modes](https://docs.originlab.com/origin-help/analydialog-recalculate/). | Closest direct precedent for marking an old fit against changed data. The cited pages establish application indicators; they do not establish that the locks are included in exported PDFs. |
| GraphPad Prism | Linked analyses and graphs normally update when input data or analysis parameters change. Freezing a results or graph sheet deliberately disconnects it from further updates and displays a note on the sheet. [Freezing sheets](https://www.graphpad.com/guides/prism/latest/user-guide/freezing_sheets.htm). | Distinguishes automatic updating from an intentionally retained version. The cited guide does not establish that its frozen-sheet note is included in a PDF export or that exports contain a hidden freshness record. |
| Excel for Microsoft 365, including Mac | In Manual and Partial calculation modes, calculated values awaiting recalculation receive strikethrough formatting. Selecting a stale cell exposes a warning and calculation actions. The formatting disappears after calculation; it can also be disabled without updating the value. [Stale Value Formatting](https://support.microsoft.com/en-us/excel/stale-value-formatting). | Strong precedent for a visible, temporary warning tied to a computed value. The guide lists coverage limits, including external data. It does not establish equivalent treatment for fitted curves or exported charts. |
| Matplotlib | `savefig` accepts metadata for supported formats. The PDF backend provides document information and `PdfPages.attach_note`; the note's default rectangle is outside the page so it is invisible on printouts. [Figure metadata](https://matplotlib.org/stable/api/_as_gen/matplotlib.pyplot.savefig.html), [PDF notes and metadata](https://matplotlib.org/stable/api/backend_pdf_api.html#matplotlib.backends.backend_pdf.PdfPages.attach_note). | Direct precedent for scientific graphics carrying a record without printed artwork. These APIs provide storage; the application must supply the meaning and freshness check. They do not promise survival through external editing. |
| OriginLab's Send Graphs to Word app | An optional command adds information about a graph's Origin source to Word alternative text. Graphs can be inserted as pictures or embedded objects. [App documentation](https://www.originlab.com/FileExchange/details.aspx?fid=238&v=0). | Concrete example of provenance accompanying a graph without a printed label. This is a Word-specific source record, not an invisible stale-fit watermark in a standalone PDF. |

Origin also advertises exporting “Analysis Marker” objects and documents the optional `@UPA` setting. That feature name must not be treated as evidence that its recalculation-status locks are exported; the [system-variable documentation](https://docs.originlab.com/labtalk/ref/sys-var-list/) does not establish that equivalence.

No inspected source establishes a built-in combination of a visible stale-fit export warning and an invisible record guaranteed to survive later editing. This is a bounded documentation finding, not evidence that no product offers one. Targeted searches for AFFINImeter, PEAQ-ITC analysis software and NanoAnalyze did not establish a comparable marking policy from the returned public sources; no absence claim is made for those products.

## Implications for a future design

The precedents support investigating a visible freshness state together with an independent provenance record. They do not justify silently refitting or replacing the selected solution. FT-ITC can retain the user's chosen fit while clearly stating whether it matches the displayed data.

A candidate visible message is “Fit does not match current data.” A short reason could identify changed processing, concentrations or injection inclusion. Whether it appears in preview and printing as well as exports, where it is drawn, and whether it can be suppressed within the application remain design decisions. It should not accidentally disappear with an unrelated parameter-box setting.

The simplest hidden candidate uses the existing PDF metadata fields. A structured record could contain the status at export, mismatch reasons, the selected model and experiment/solution identifiers, and the export time. Input fingerprints and an additional PDF page note are options to evaluate, not agreed requirements. Shared Core logic should define the meaning, with both existing desktop renderers retained.

Hidden metadata or a page note may remain after deleting a visible label, but neither is permanent. Acrobat explicitly allows removal of metadata, comments and hidden content. A screenshot cannot carry non-rendered PDF objects, and conversion to a new document may omit them. Survival in Illustrator, Affinity Designer, Inkscape, Word and PowerPoint must be measured for the chosen workflow. [Adobe sanitization documentation](https://helpx.adobe.com/acrobat/desktop/protect-documents/redact-pdfs/sanitize.html).

The record would describe freshness at export time; a previously exported PDF cannot update itself when the project changes later. Missing metadata must not be interpreted as proof that a figure is current. An ordinary identifier or hash is not authentication, and preserving a record is different from preventing its removal.

## Decisions to revisit before implementation

- Define which changes invalidate the displayed fit and how to obtain sufficient captured evidence for standalone fits. Keep known mismatch, unknown freshness, fit-health warnings and assessment/uncertainty limitations distinct.
- Resolve the exact plotted Binding or Null solution and its dependencies. Consider independent members versus a coupled global fit. An older solution can still match its inputs; being older than another fit is not itself a mismatch.
- Decide how model-free figures and figures using fitted quantities without a visible curve should be treated. Do not mark a figure solely because an unused stale solution happens to be attached.
- Agree whether exports remain allowed with marking, require an explicit choice, or are blocked in particular cases. Reconcile the current associated-export platform difference within that decision; logging it does not approve a fix.
- Choose visible wording and placement, whether hidden records are written for all statuses or only mismatches, and how users or reviewers inspect the record. Decide whether freshness marking belongs to ordinary output or has any relationship to Traceability Mode.
- Select the particular applications and export methods for the confirmed priorities: vector editing first, then placement into a presentation or manuscript. Test the exact export/delete-label/re-save/placement path before making any persistence claim. Metadata and optional page notes are the initial candidates; a robust artwork watermark would be a separate investigation. Do not alter plotted scientific coordinates, fit values or uncertainties to encode a mark.
- After those decisions, define focused tests for changed and reverted inputs, missing evidence, Binding/Null/None selections, independent and coupled results, both desktop export paths, and agreement between the visible and hidden statuses captured for one export. Update user documentation only when behavior is implemented and helps users interpret or share a figure.

No application code, scientific behavior, export eligibility, project format, manual, version or release metadata was changed for this registration.
