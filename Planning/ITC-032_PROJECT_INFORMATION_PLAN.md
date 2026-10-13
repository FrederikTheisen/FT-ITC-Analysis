# ITC-032: Project Information and saved-report management (revised)

Revision of the original ITC-032 plan after review (2026-10-12). Also resolves ITC-031.

## Summary

Add **File > Project Information…** to both desktop apps. It lists experiments, Analysis Results, and saved reports, and offers **Open**, **Rename**, **Delete**, and a read-only details view for reports. A report is identified by its set of result IDs. Its supporting-experiment selection is saved state of the report rather than part of its identity.

## Window

- Modal: a dialog in Avalonia and a sheet on macOS. While it is open, no report window can be open and the project cannot change underneath it.
- The header shows the project filename, or “Unsaved project”. Three tabs: **Experiments**, **Analysis Results**, **Saved reports**, each with a count. Counts include inactive experiments and exclude experiment copies held inside results.
- Experiments: name, then date and time.
- Analysis Results: name, model, experiment count, then date and time.
- Saved reports: display name, number of results, **Last edited** date and time, and a “1 of 2 results missing” status where applicable.
- A short note explains that saved reports hold selections, settings, study context, and approved interpretation text, not PDFs or copies of measurements.
- Available for empty and report-only projects.
- **Open** closes Project Information and opens the Analysis Report tool on that exact report. Closing the report tool returns to the main window.

## Names

- There is one name field. A nonempty `Name` is shown unchanged. This includes names such as “Analysis report” and “{result} report” written by earlier versions.
- New reports start with an empty name.
- Move the automatic title logic, now duplicated in both report windows, into Core. One result gives the result's name; several give “Analysis report”.
- Display name of an unnamed report: “{result name} report” when it has exactly one resolved result, otherwise “Analysis report”. The result count beside it distinguishes multi-result reports.
- PDF title fallback when the title field is blank: use the Core automatic title instead of `report.Name` (`AnalysisReportBuilder.cs:178`). A rename never reaches the PDF.
- AI interpretation request: send the display name instead of the raw name, so blank names don't produce an empty value. (Assumption; confirm.) A rename must not affect interpretation freshness.
- **Rename** edits the stored name. Clearing it restores automatic naming.

## Report matching and selection

- Reports are matched by equality of their result-ID sets. Order is ignored.
- The PDF keeps following project result order. Reordering the project neither rewrites stored result IDs nor advances **Last edited**.
- Ordinary opening (Tools menu, result menus) loads the matching report with the latest creation date. When creation dates are equal, the last matching collection entry wins. Older variants from earlier versions are kept and can be opened from Project Information.
- Opening a saved report restores its supporting selection. Automatic reference selection applies only when a new report is created.
- Picker **Apply**:
  - Results unchanged: the picker's supporting selection is saved to the current report.
  - Results changed: load the matching saved report with its own supporting selection, or create a new report with automatic references. Supporting changes made in the same Apply are ignored.
- Registration triggers are unchanged: a nonempty supporting selection (including automatic selection at opening), settings changes, and saved interpretation text.
- Interpretation text never carries over from one result set to another.

## Reports with missing results

- A report with at least one missing result cannot be opened. **Open** and **Rename** are disabled; **Delete** remains available.
- These reports are retained unchanged. No report window can match them, because their ID set contains an ID that no longer exists.
- The list shows the missing count. Result names are not stored, so missing results cannot be named.

## Report details

- A read-only details view is available for every report, whether its results are present or missing.
- It shows: display name, creation and last-edited date and time, results (names when present, “Missing result” otherwise), supporting experiment count, title, document label, report ID, author comments, non-empty study context fields, and approved interpretation text.
- Text is selectable. **Copy All** copies the user-entered text as plain text.

## Deletion

- Delete one report at a time after confirmation. The confirmation names the report and states that its saved selections, settings, context, and interpretation are removed. No Undo.
- Use `DataManager.RemoveReport` and the existing dirty-state tracking. Experiments, results, and exported PDFs are unaffected.

## Last edited

- Add nullable `LastEditedUtc` to `AnalysisReport` and optional `lastEditedUtc` to `.ftxtc` report metadata. Package and report schema versions are unchanged.
- New reports start at their creation time.
- Advance only on an actual change to stored content: name, supporting selection, comments, study context, interpretation settings or text, or presentation settings.
  - This requires value comparisons for study context and interpretation options. Presentation settings already have `PresentationSettingsEqual`.
- Do not advance on: identical assignments, opening, previewing, exporting, saving, freshness evaluation, restoration (respect `IsRestoringDocument`), or detached copies. Copies keep the timestamp.
- Older reports show **Not recorded** until their first real edit.
- Display with `LocalDateTimeFormatter.ShortDateTime`.

## Shared code and text

- Core: display-name and automatic-title helpers, result-set matching/selection helper, and missing-result status.
- Both report entry points accept an exact saved report.
- Labels and tooltips are defined in Core and follow the capitalization rules.

## Validation

- Core:
  - automatic and custom names; result rename updating automatic names; PDF title fallback;
  - set matching across order; newest-created selection and tie-break; legacy variants retained;
  - supporting edits keep the report's identity; missing-result status.
- Timestamps:
  - each persisted edit advances it; unchanged operations, project reordering, restore, and copy do not;
  - save/reopen preserves it; older files read without it.
- Both desktop apps:
  - counts; empty and report-only projects; exact older-variant opening;
  - rename cancel and clear;
  - delete cancel and confirm;
  - missing-result reports cannot be opened or renamed but can be viewed, copied, and deleted;
  - removed supporting selections stay removed on reopen;
  - long names and scrolling.
- Run the Core suite, the relevant Avalonia tests, and native managed/UI checks. No packaging.

## Documentation

- Manual: Project Information; update `09-figures-printing-export.md` lines 112 and 136 (reopening behavior); shared help.
- `FTXTC_FORMAT.md` and the schema: optional `lastEditedUtc`.
- `ANALYSIS_REPORT_MAP.md`: title fallback.
- Resolve ITC-031. Resolve ITC-032, noting that limits and size display were deliberately not implemented.

## Out of scope

- Storage limits, size reporting, full metadata browsing, batch deletion, new preferences.
- The control for including supporting experiments in printed reports (ITC-091).
- The preference control for automatic reference selection (ITC-033).
