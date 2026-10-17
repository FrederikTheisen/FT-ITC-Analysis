# CR-001 and CR-070: focused simplification

## Summary

Implement three bounded changes: remove duplicate CR-001 button-state evaluation, remove unsupported native alert-close handling from the Final Figure overwrite prompt, and create export temporary files in one operation.

Preserve fitting safeguards, export guarantees, existing status-bar timing and current inspector-footer behavior. Implementation should use Luna agents.

## Decisions taken in review

- **Duplicate-import prompt is out of scope.** `AnalysisITC.MacOS/Platform/MacFtxtcDuplicatePromptService.cs` uses the same `WillCloseNotification` → `AbortModal` observer, but it also owns a key-event monitor, removes its observer explicitly, and has its own harness (`FtxtcDuplicatePromptTests`). Changing it is not part of CR-070. Record it in `Planning/ISSUES.md` (step 4) instead of resolving it.
- **`alert.Layout()` is removed** together with the `alert.Window` lookup. Both existed only to supply the close observer's window; `RunModal` and `RunSheetModal` lay the alert out themselves, and the injected-response harness inspects style, buttons and text, not layout.
- **No redundant "stream closed before commit" test.** The test double's `TempStream` stores its bytes only on `Dispose`, so the existing content assertions already fail if commit precedes disposal.

## Implementation changes

### 1. CR-001: avoid redundant readiness evaluation

In `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs`:

- Remove the direct `UpdateFitButtonState()` call following `UpdateStatus()` in `RefreshWorkspaceViews()`. Every `UpdateStatus()` branch already ends in `UpdateFitButtonState()`, and `graph.IsReadOnly` is still set before `graph.InvalidateVisual()`.
- Reorder `canFit` evaluation to:
  ```csharp
  !isFitting
      && !contextRebuildDeferred
      && experiment != null
      && CurrentFitInputsAreReady()
  ```
  `experiment != null` duplicates a check inside `CurrentFitInputsAreReady()`; keep it deliberately as a cheap early exit.
- Retain `CurrentFitInputsAreReady`, context identity/model/mode/member-order checks, deferred rebuilding, completion reconciliation and the independent preflight before preparing a solver.
- Keep graph read-only behavior and calculation-admission handling unchanged.
- Leave completion-message preservation and its clearing rules untouched. Make no changes to status-bar durations, timers or completion reporting.

This is a reduction in duplicate evaluation, not a change to fitting eligibility.

### 2. CR-070: remove unsupported alert-close handling

In `AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs`, simplify the injectable `ConfirmOverwriteIfNeeded(NSWindow, ISet<string>, Func<NSAlert, NSWindow, IntPtr>)`:

- Remove `alert.Layout()`, the `alert.Window` lookup, the `WillCloseNotification` observer, its `AbortModal` callback and the observer-disposal `try/finally`.
- Return `showModal(alert, parent) == (IntPtr)(long)NSAlertButtonReturn.First` directly.
- Preserve alert style, message, informative text, button labels and order, sheet versus application-modal presentation, and approval exclusively for `NSAlertButtonReturn.First`.
- Retain Cancel and unexpected-response rejection.
- In `AnalysisITC.MacOS.Tests/FinalFigureModelSelectionTests.cs`, rename the `IntPtr.Zero` case's `closed` variable and its assertion wording to describe an unexpected modal response declining replacement. Do not describe it as closing a real alert or add close-event simulation.

### 3. CR-070: acquire the temporary path and stream together

In `AnalysisITC.Core/DataExport/ExportBatchWriter.cs`:

- Change the internal interface member to:
  ```csharp
  (string Path, Stream Stream) CreateTemporarySibling(string destination);
  ```
- Remove `OpenTemporary` from the interface, the physical implementation and the test double.
- Generate one GUID-named sibling (existing `.<name>.<guid>.tmp` naming) and open it once with `FileMode.CreateNew`, `FileAccess.Write` and `FileShare.None`. Return the tuple only after successful creation.
- Remove the 32-attempt collision loop and its "Could not reserve" message. Creation failures use the existing handled batch-failure path.
- Update `Write` and `WriteAsync` to record the returned cleanup path and pass the returned stream to the existing `WriteAndClose` / `WriteAndCloseAsync` helpers.
- Assign the cleanup path only after acquisition succeeds, so failure cannot delete a file the writer never created.
- Preserve disposal before commit, first-exception preservation, cleanup-error suppression and separate synchronous/asynchronous execution.

Public export APIs remain unchanged. Retain filename planning, overwrite authorization, protected replacement, no-overwrite moves, stop-on-first-failure behavior, partial counts and failure messages. Introduce no new ownership classes or filesystem abstractions.

### 4. Issue tracker

Add one `Planning/ISSUES.md` entry noting that `MacFtxtcDuplicatePromptService.ChooseAction` registers a `WillCloseNotification` → `AbortModal` observer on the alert window, which an `NSAlert` cannot trigger by user action. Record it as an open question, not a resolution.

## Validation and acceptance

Rely on existing workflow tests; add only the one Core case below.

- **CR-001:** verify successful and cancelled A→B navigation, latest selection, unready/result/no selection, global member ordering, retained A outputs and parameter overrides. Verify existing completion-feedback behavior.
- **CR-070 native:** retain Cancel/Overwrite/unexpected-response checks, alert configuration checks, real file/PDF writes, approved replacement, late unapproved destinations and partial batches.
- **CR-070 Core:** adapt `MemoryExportFileSystem` to return `(path, TempStream)` and add a `FailCreate` flag. Add one case, parameterized over `Write` and `WriteAsync`: when acquisition fails, the outcome is a handled failure with the destination as `FailedPath`, zero completed files, and neither the write callback nor `DeleteTemporary` is invoked. Existing write/flush/disposal/cleanup failure and content tests cover the remaining behavior.

After focused checks, run the full Core and full Avalonia suites, then the native harnesses:

```sh
dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --configuration Release --no-restore -m:1 -nodeReuse:false

dotnet build AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj --configuration Release --no-restore -m:1 -nodeReuse:false -p:UsedAvaloniaProducts=

dotnet AnalysisITC.Avalonia.Tests/bin/Release/net10.0/AnalysisITC.Avalonia.Tests.dll -parallel none -xml /private/tmp/cr001-cr070-avalonia.xml

bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh FinalFigureModelSelectionTests ProtectedNativeExportTests SupportingFigureCanvasTests
```

Run build/test stages sequentially to avoid competing over shared outputs. Record logs, test counts and limitations. Full-suite totals include unrelated uncommitted working-tree changes; if failures occur, distinguish regressions from existing failures before changing anything outside scope. Automated native harnesses must not be reported as an interactive desktop walkthrough.

## Constraints and handoff

- Preserve unrelated working-tree changes; inspect the final scoped diff and run `git diff --check`.
- No ITC-109 changes, scientific calculation changes, serialization changes or inspector-feedback redesign.
- No changes to `MacFtxtcDuplicatePromptService` or `FtxtcDuplicatePromptTests`.
- No manual/help additions for these internal simplifications.
- In `Planning/CODE_REVIEW_2026-10.md`, correct the CR-070 native wording "declined/closed confirmations" and "Cancel/closed/Overwrite results" to describe injected Cancel, Overwrite and unexpected responses. Leave the Avalonia "dialog closure" wording (CR at line 92) unchanged, since Avalonia windows can be closed.
- No packaging, signing, release work, version changes, commits or pushes.
- Deliver the implementation summary and validation evidence for review.
