# Fit progress, completion messages and one calculation at a time (ITC-098, ITC-100)

Revised 15 October 2026 from Codex's plan ("Improve fitting progress and completion messages") after review with the user. It covers Run Fit and Update Result on both desktop apps and preserves the completed ITC-099 behaviour. The status-bar progress indicator is a separate issue, ITC-109. The message wording below is a working draft that the user is still refining. Define every string once in Core so wording changes touch one place.

## Decisions

- **One calculation at a time** (user-approved scope addition to ITC-098). While any fit, result update, uncertainty estimate, advanced analysis or processing operation is running, the app does not start another. See section A.
- **Experiment names:** keep the shared code flag `FittingStatusPresentation.ShowExperimentNames` (default `true`). It exists so names can be switched off if they look poor in practice. There is no Preferences control or persisted setting. Names appear only when experiments are fitted individually; global fits never name a member.
- **Global fits** use the same messages as a single-experiment fit. Only the first message states the number of experiments.
- **Running messages** put the action first, then the position, then the experiment name, for example "Fitting 2 of 3: Sample B" (section D). The status bar shortens long text at the end on both platforms, so a long name is cut first and the action and position stay visible.
- **Completion messages** describe the whole run as an "Analysis", not a "Fit", because the run also covers binding assessment and uncertainty estimation. Update Result keeps "Result updated".
- **Routine completion** is at most two short messages. A parameter-bound warning may add a third.
- **Progress indicator** style and behaviour are out of scope and tracked as ITC-109. Leave the indicator unchanged here: keep feeding it the existing progress fractions, and show the counter in the existing secondary status.
- **Durations** rarely exceed a few minutes, and more than 10 minutes is unusual. The format in section E is enough; there are no hour units.
- **Manual and help** get one lean section. Section F gives the content to cover, not verbatim text.

## Code facts this plan relies on

Checked against the current source:
- `SolverConvergence.TotalTime` is fit time plus uncertainty time.
- `ProfileLikelihoodProgress` exposes `CompletedSides`/`TotalSides`.
- RMSD remains visible in the analysis inspector (`AnalysisParameterSummaryPresentation`).
- Core already has a completion builder in `UncertaintyEstimationPresentation` (`Completion`, `UpdateCompletion`, `UpdateStatus`). Extend or replace it rather than adding a parallel builder.

Progress currently travels through static `SolverInterface` events:
- `SolverUpdated` is raised with a null sender, so listeners cannot tell which operation sent it.
- Avalonia's dispatcher posts background-thread events to the UI thread asynchronously; macOS invokes them on the main thread.
- Bootstrap refits run in `Parallel.For`.

Run Fit (Avalonia `isFitting`, macOS `isFitting`), Update Result (`isUpdatingResult`) and the experiment designer Fit are each guarded only by their own flag. They can therefore run concurrently and mix status messages.

## A. One calculation at a time

- Add atomic admission to `ProcessingActivity`, for example `TryBegin()`, which returns a lease or nothing. A double click or two windows must not both start. Use it at every user entry point that starts a solver or processing operation:
  - Run Fit;
  - Update Result (Results tab and menu);
  - experiment designer Fit;
  - advanced analyses;
  - tandem mixing search;
  - any other `ProcessingActivity.Begin` entry point found in the inventory.

  Inventory these on both platforms before changing them.
- Disable those start controls while `ProcessingActivity.IsBusy` and re-enable them on `ProcessingActivity.Changed`. Stop/Cancel for the running operation stays available.
- When a start control is disabled for this reason, its tooltip says so. Define the text once in Core, in 10 words or fewer, for example "Unavailable while another calculation is running."
- Background processing, for example after loading a project, also holds the gate, so fitting waits for it. That is the intended behaviour.
- Even with this rule, tag every progress and completion event with its owning operation. Ignore events from an older or unrelated operation, such as a stale Avalonia post that arrives after a new start.

## B. Progress reporting

- **Payload.** Add an immutable, transient Core progress payload carrying:
  - owning operation;
  - phase (fitting, assessing binding, estimating uncertainty, stopping);
  - scope (single, individual, global);
  - member position and name;
  - uncertainty method;
  - counter and fraction.

  Capture values before dispatching. Parallel refits must not share a mutable counter object that is read later on the UI thread.
- **Transitions.** Publish at actual phase transitions. Forward silent member progress through the owning global solver.
- **Execution order.** Report the real calculation order; do not change it. Order confirmed in `Solver2.cs` on 15 October:

  | Fit type | No-binding uncertainty skipping on (default, `SkipsNoBindingUncertainty`) | Skipping off |
  | --- | --- | --- |
  | One experiment | fit → assess binding → uncertainty | fit → uncertainty → assess binding |
  | Several, fitted individually | fit 1…n → one assessment covering all experiments → uncertainty for each eligible experiment | fit and uncertainty for experiment 1, then 2, … n → one assessment covering all experiments |
  | Global | fit → assess binding → uncertainty | fit → uncertainty → assess binding |

  Individually fitted experiments are never assessed one at a time, so there is no per-experiment assessment message.
- **Delivery.** Remove experiment-number inference from overall percentages, and remove competing legacy status writes, so that only the shared payload reaches the status bar.
- **Update Result.** Clear progress at solver completion, but publish terminal messages only after the updater has decided between replacement and retention.

## C. Progress indicator

Out of scope; tracked as ITC-109. Do not change the indicator's style, its percentage label or its fraction behaviour in this work. The progress payload keeps the existing fractions so that ITC-109 can decide their presentation later. Show the counter through the existing `StatusBarManager.SetSecondaryStatus`. Restore the previous status after completion.

## D. Messages (draft wording; the user is refining it)

### While running: primary status

Each message gives the **action**, then the **position** (individual fits only), then the **experiment name**. Core provides the three parts separately. The platforms show them as one string. Both status bars shorten long text at the end (Avalonia `CharacterEllipsis`, macOS `truncatingTail`), so a long name is cut first while the action and position stay visible.

| Phase | One experiment | Several, fitted individually | Global fit |
| --- | --- | --- | --- |
| Fitting | Fitting: Sample A | Fitting 2 of 3: Sample B | Fitting 3 experiments globally… |
| Assessing binding | Assessing binding: Sample A | Assessing binding… (one step for all experiments) | Assessing binding… |
| Estimating uncertainty | Estimating uncertainty: Sample A | Estimating uncertainty 2 of 3: Sample B | Estimating uncertainty… |
| Stopping | Stopping analysis… | Stopping analysis… | Stopping analysis… |

- **Global fits** name no member. Only the first message gives the number of experiments.
- **Endings:** messages that end with a name have no ellipsis; messages without a name end with "…".
- **With `ShowExperimentNames` off,** the name and its colon are dropped:
  - individual fits read, for example, "Fitting 2 of 3";
  - a single experiment shows the action alone, for example "Fitting…".
- **Update Result** uses the same lines.

Messages follow the real order in section B. For experiments fitted individually:
- **Skipping on (the default):**
  1. "Fitting 1 of 3: …" through "Fitting 3 of 3: …";
  2. "Assessing binding…" once;
  3. "Estimating uncertainty 1 of 3: …" for each eligible experiment.

  Experiments skipped for no binding do not appear in step 3. Positions keep their original numbering, for example "Estimating uncertainty 3 of 3: Sample C" even if Sample B was skipped; they are not renumbered among the eligible experiments.
- **Skipping off:**
  1. "Fitting 1 of 3: …", then "Estimating uncertainty 1 of 3: …", and so on for each experiment;
  2. "Assessing binding…" once at the end.

For one experiment or a global fit, assessment comes before uncertainty when skipping is on, and after it when skipping is off.

### While running: secondary status (counter)

`Bootstrap 40/100`, `Leave-one-out 3/24`, `Profile searches 3/8`. Profile searches count completed search directions (`CompletedSides`/`TotalSides`), whether or not an interval endpoint was found.

### When finished: message 1, outcome and total time (3 s)

The message describes the whole run (fit, binding assessment and uncertainty estimation), so it says "Analysis" rather than "Fit". Only an optimizer limit names the fit, because it concerns the fit itself.

| Outcome | Run Fit | Update Result |
| --- | --- | --- |
| Converged | Analysis complete in 12 s | Result updated in 12 s |
| Iteration, evaluation or time limit | Analysis complete in 12 s; fit stopped at the iteration limit | Result updated in 12 s; fit stopped at the iteration limit |
| Cancelled | Analysis cancelled after 12 s | Update cancelled; the existing result was kept |
| Failed or invalid values | Analysis failed: *reason* (5 s) | Result update failed: *reason* (5 s) |
| Retained (ITC-099) | not applicable | the existing retention reason (5 s) |

A fit that stopped at a limit must not read as an ordinary success.

### Parameter-bound warning (5 s, only when needed, between messages 1 and 2)

Keep the existing `ParameterBoundaryWarningFormatter` text and its ITC-053 filtering, for example "Warning: parameter bound reached: Kd (upper, Sample B), N (lower)".

### When finished: message 2, uncertainty outcome (3 s)

Omit it when no uncertainty method is selected, and after a failed or retained update. Never derive it from an unchanged old result.

| Outcome | Bootstrap | Leave-one-out | Profile likelihood |
| --- | --- | --- | --- |
| Complete | Bootstrap complete | Leave-one-out complete | Profile likelihood complete |
| Incomplete | Bootstrap incomplete: 87 of 100 refits | Leave-one-out incomplete: 22 of 24 refits | Profile likelihood incomplete: 6 of 8 interval endpoints found |
| Unavailable | Bootstrap unavailable | Leave-one-out unavailable | Profile likelihood unavailable |
| Cancelled | Bootstrap cancelled | Leave-one-out cancelled | Profile likelihood cancelled |
| Skipped | Bootstrap skipped: no binding detected | Leave-one-out skipped: no binding detected | Profile likelihood skipped: no binding detected |
| Mixed (several experiments) | Bootstrap: 2 complete, 1 skipped (no binding) | Leave-one-out: 2 complete, 1 incomplete | Profile likelihood: 2 complete, 1 incomplete |

Remove RMSD, optimizer name, iteration count and raw diagnostic strings from completion messages. RMSD stays in the inspector and details.

## E. Durations

- Below 10 s: one decimal, for example `2.3 s`.
- 10–59 s: whole seconds, for example `48 s`.
- 60 s and above: minutes and seconds, for example `4 min 10 s`.

Use the same format at any length.

## F. Manual and in-app help

Add one short section to the fitting page of the manual source, with a matching short help entry. Choose the wording and structure at writing time; keep it lean and user-facing. It should convey:
- While a fit runs, the status bar reports what is being calculated: the experiment and its position when fitted individually, binding assessment, and uncertainty estimation with a count of completed refits or profile searches.
- When it ends, the status bar shows the outcome and total time, then the uncertainty outcome.
- Some outcomes need checking before the result is used: a fit stopped at a limit, or uncertainty reported as incomplete, unavailable or skipped.

Do not document durations, message timing, execution order, the name flag, the one-calculation-at-a-time rule or other expected UI behaviour.

## G. Tracker and priorities

- **ITC-098 and ITC-100:** update both entries with the resolution, recording the one-calculation-at-a-time addition as user-approved scope for ITC-098.
- **`Planning/FUNCTIONALITY_PRIORITIES.md`:** update the ITC-098/100 row.
- **Report map:** check `Documentation/ANALYSIS_REPORT_MAP.md` only if report content changes. It should not.

## Validation

- **Event sequences:** test actual sequences for single, independent (both execution orders) and global fits, with bootstrap, leave-one-out, profile likelihood and no uncertainty. Cover:
  - skipped members, mixed outcomes and partial profiles;
  - iteration, evaluation and time limits;
  - cancellation, failure and missing completion information.
- **Concurrency:**
  - immutable counters across delayed dispatch and parallel refits;
  - correct member numbering;
  - names on and off, and a long experiment name in the status bar;
  - operation ownership, and stale events after a new start;
  - secondary-status restoration.
- **One calculation at a time:**
  - start controls disabled while a fit, update, designer fit, advanced analysis or processing is active;
  - atomic admission refuses a second start;
  - Stop stays available;
  - controls re-enable when idle;
  - the disabled-state tooltip.
- **Messages:**
  - running messages in both execution orders, with original positions kept in the uncertainty pass;
  - completion message order, durations, absent routine metrics, preserved warnings and the duration format.

  Keep all ITC-099 replacement, retention, dirty-state and evaluation-temperature checks.
- **Progress indicator:** unchanged by this work (ITC-109). Its existing behaviour must not regress.
- **Suites:** run focused regressions, the full Core and Avalonia suites, native managed compilation, and AppKit checks for Run Fit and both Update Result entry points.

## Boundaries

- Transient reporting and start admission only. Preserve equations, solver settings, execution order, uncertainty methods, the result-replacement policy and project serialization.
- No packaging, release metadata, version or citation edits, commits, pushes or publication operations. The user commits after review.

## Working method

Use subagents for independent parts of the work. Settle the Core progress payload, wording table and admission API in the main agent first. Then hand the Avalonia integration, the macOS integration, and the test authoring to separate subagents working against that fixed Core contract. Before reporting completion, run a separate review subagent that checks the finished change against this plan.
