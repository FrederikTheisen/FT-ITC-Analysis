# Standalone fit snapshots and Final Figure export warning

Status: Focused plan, 23 October 2026. Planning only; implementation is not authorized.
Execution when approved: scoped gpt-6-luna agents.
Tracker: [ITC-124](ISSUES.md#itc-124---mark-final-figures-when-the-displayed-fit-no-longer-matches-the-data).

This narrows the [original investigation](ITC-124_FINAL_FIGURE_FRESHNESS.md) to an export warning. No artwork/renderer changes, hidden records, new preferences or invalidation redesign.

## Behavior and open assumptions

- Compare the selected fit's original inputs with current inputs before PDF export. Matching fits export normally; a mismatch produces a warning. Its UI has not been decided.
- Restoring all original inputs clears the warning. Keep this comparison separate from the sticky `IsValid` flag, fit health, assessment and uncertainty availability.
- Check the selected Binding or Null solution. Skip output using no fitted quantities. Preserve independent-member versus coupled-group comparison scope.
- Cover single, selected-set, Active, All and associated-result exports in both apps. Preview, printing, clipboard and reports are outside scope.

User clarification, 23 October 2026:

- **Agreed:** a separate warning for a nonexistent snapshot is acceptable. Keep it very simple: absent snapshot means freshness cannot be verified. Never manufacture fit-time evidence during load/export.
- **Open:** UI presentation, wording, actions, placement in the export workflow and batch handling. **Export Anyway / Cancel** was a proposal, not an agreed design. The user is not ready to choose the UI yet; defer this decision.
- **Open:** whether macOS's existing associated-result export block becomes a warning. Do not remove or replace it without that decision; preserve unrelated eligibility rules.

## Implementation

1. **Capture in Core.** Reuse `ExperimentFitInputSnapshot.Capture/Compare` and `AnalysisResultValiditySnapshot` group logic. Retain original inputs with accepted primary solutions, preserving coupled-fit context and reusing Analysis Result evidence where available. Avoid generic `SolutionInterface.FromModel` capture: restoration and uncertainty replicates use it too. No snapshots for bootstrap/profile trial fits.
2. **Save/load.** Store optional standalone snapshots using existing FTXTC validity wire types. Preserve evidence through solution transfers and remap references during Import Copies. Existing `.ftxtc` and read-only `.ftitc` imports remain readable; missing evidence is unknown. Read/update the native format specification and schema as required. No application-version changes.
3. **Shared check.** Use the selected source resolved by `PublicationFigureBuilder.ResolveModel`; return matching, changed or unknown. Preserve existing comparison tolerances. Leave other validity consumers unchanged; no extra machinery for missing evidence.
4. **Desktop warning, after UI agreement.** Connect Avalonia `FinalFigureWorkspaceControl` and macOS `FinalFigureGraphView` to the shared check for the actual output selection. Prefer existing UI components and batch handling. Keep shared warning text in Core. Decide the UI and associated-export policy before implementing this step; add brief manual/help text once behavior is settled.

Core entry points: `AnalysisResultValiditySnapshot.cs`, `Models.cs`, `Solver2.cs`, `FTXTCFormat.cs`, `FTXTCReader.cs`, `FtxtcImportResolver.cs`. Format specification: `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md`.

## Focused validation

- Fail-first change/revert tests: injection inclusion, temperature, concentrations, integrated heats and a fit-relevant attribute. Restoring inclusion while another input differs must still warn.
- Save/reopen and Import Copies preserve original evidence; missing evidence stays unknown; uncertainty replicates acquire no extra snapshots.
- Cover Binding, saved Null, model-free, independent and coupled outputs using the actual selected fit.
- Both desktop paths: matching, changed and missing-snapshot cases, mixed batches and associated exports. Add action/cancellation tests for the UI once agreed; check the fit actually being exported.
- Run `AnalysisITC.Core.Tests`, focused Avalonia tests and native managed export checks; build both apps and inspect their implemented warnings. No packaging, releases, commits or pushes.

Done when restored inputs clear the warning before/after reopening and both apps follow the confirmed policy without changing figures or fitted values.
