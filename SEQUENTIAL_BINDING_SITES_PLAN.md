# Sequential Binding-Sites Model

## Summary

Implement a configurable two- to four-step sequential binding model, defaulting to two steps. The model will fit macroscopic stepwise association constants \(K_i\), molar step enthalpies \(\Delta H_i\), and the same molar offset parameter used by the existing binding models. It will not fit N-values or syringe activity.

The scientific formulation follows the documented MicroCal sequential model and reports phenomenological/macroscopic, rather than microscopic intrinsic, constants. The implementation must support single-experiment and global fitting, persistence, bootstrap uncertainty, results, export, the web viewer, Avalonia, and macOS without broadening the scientific scope of the model.

## Scope Guardrails

In scope:

- Two to four sequential binding steps with a shared step count in global analyses.
- Step-specific affinity and enthalpy values.
- One family-level global constraint style for all active affinities and one family-level global constraint style for all active enthalpies.
- Existing offset behavior and its existing global constraint behavior.
- Ordinary per-step thermodynamic results, uncertainty, temperature evaluation, plots, exports, and viewers.
- Infrastructure changes required to represent and render up to four thermodynamic slots safely.

Out of scope:

- Fitted N-values, syringe activity, intrinsic-statistical conversion, or microscopic-site claims.
- Reverse titrations with macromolecule in the syringe.
- Extending the Spolar Record method, protonation, or electrostatics analyses to sequential models.
- Changing the numerical implementation of `TwoSetsOfSites` unless a separately demonstrated defect requires it.
- Folding affinity display-unit policy into the scientific model. That question is assigned to a separate workstream below.
- Adding a new preference for affinity units unless the dedicated investigation recommends it and the change remains small and isolated.

## Scientific and Public Interface Contract

- For step count \(n\), define \(\beta_0=1\), \(\beta_i=\prod_{j=1}^{i}K_j\), weights \(w_i=\beta_i x^i\), state fractions \(F_i=w_i/\sum_{j=0}^{n}w_j\), and mean occupancy \(\bar\nu=\sum iF_i\).
- Solve the monotonic ligand balance
  \[
  X_t=x+M_t\bar\nu(x)
  \]
  on \(0\le x\le X_t\) using bracketed bisection. Evaluate weights using log-sum-exp and handle \(x=0\) explicitly.
- Keep bisection entirely internal; it is not a UI option. Use a very tight scale-aware termination rule, for example an interval or mass-balance residual no larger than
  \[
  \max(10^{-24}\ \mathrm{M},\ 10^{-14}\max(X_t,nM_t)).
  \]
  Also terminate if floating-point resolution can no longer produce a distinct midpoint, and retain a conservative iteration cap. Verify this tolerance against the identical-site equivalence tests before finalizing it.
- Calculate cell heat content as
  \[
  Q=VM_t\sum_{i=1}^{n}F_i\left(\sum_{j=1}^{i}\Delta H_j\right)
  \]
  and convert consecutive heat contents to injection heat through the existing `DeltaHeatFromHeatContent` displaced-volume correction.
- Implement offset exactly as in the other binding models: add `Offset * InjectionMass` to the calculated injection heat when `withoffset` is true. Do not introduce a constant-joules-per-injection interpretation.
- Store affinities internally as `log10(Ka_i)`; report \(K_{d,i}=1/K_i\), \(\Delta G_i=-RT\ln K_i\), and \(-T\Delta S_i=\Delta G_i-\Delta H_i\).
- Do not sort fitted steps. Step numbers identify the \(M\to MX\to MX_2\ldots\) transitions and are not exchangeable independent-site labels.
- Append, without reordering existing enum members:
  - `AttributeKey.SequentialSiteCount`.
  - `ParameterType` families for steps 3 and 4: affinity, enthalpy, Gibbs, heat capacity, entropy, and entropy contribution.
- Persist the site count as an integer with stable FTXTC ID `sequential-site-count`; accept only 2-4. A missing count defaults to 2 only when reading a context in which omission is valid. A genuine persisted sequential solution must have an explicit, shape-consistent count.
- Retain the existing `AnalysisModel.SequentialBindingSites` ordinal and `sequential-binding-sites` wire ID.
- Before persistence implementation is finalized, explicitly decide whether genuine sequential solutions use model-schema version 2. This is recommended because model-schema version 1 names the dormant fallback rather than a genuine sequential solution. A package-schema bump is not expected. Document and test whichever compatibility rule is selected; never silently interpret a malformed sequential payload as dissociation.

## Required Agent Workstreams

These are explicit steps in the implementation effort. The agents should investigate and plan before broad code changes are made. Their outputs must be reviewed and integrated into the main implementation plan; agents must not make overlapping edits concurrently.

### A. Global affinity constraint semantics

Before implementing sequential global constraints, launch a separate planning agent using `gpt-5.6-sol` with high or greater reasoning effort.

The agent must:

- Inspect existing single- and multi-experiment constraint construction, propagation, persistence, bootstrap restoration, correlations, and UI state.
- Produce a focused plan that distinguishes:
  - `SameForAll`: one shared `log10(Ka_i)`/`Affinity_i` coordinate per step, producing the same \(K_{d,i}\) in every member experiment.
  - `TemperatureDependent`: one shared \(\Delta G_i\)/`Gibbs_i` coordinate per step, evaluated at each experiment temperature to produce temperature-specific \(K_{d,i}\).
- Preserve a separate shared coordinate for each sequential step. Applying one affinity-family constraint style must not force \(K_1=K_2=\ldots\); it only applies the same style across the active steps.
- Specify regression tests for existing one-set and two-set behavior as well as new sequential behavior.
- Identify whether the general affinity-constraint correction should land before the sequential model or as an isolated prerequisite commit.

After the plan is accepted, the Sol planning agent may delegate the bounded implementation to another model agent. The implementing agent must follow the approved tests and file boundaries, while the primary agent remains responsible for integration and full-suite verification.

### B. Hard-coded two-slot surface audit

Before editing result, export, and UI surfaces, launch a separate `gpt-5.6-sol` planning agent to perform a repository-wide audit.

The audit must cover Core, persistence, bootstrap/correlation code, exports and clipboard paths, the web viewer, Avalonia, and macOS. It must find explicit `Affinity1/Affinity2`, `Enthalpy1/Enthalpy2`, Gibbs, entropy, and heat-capacity lists or switches and classify each occurrence as:

- Replace with indexed family iteration.
- Keep intentionally because the feature is One-Set-Of-Sites-only.
- Keep as a compatibility boundary with an explanatory comment or test.
- Defer because it is unrelated to sequential results.

The deliverable is a file-level implementation plan and a small set of reusable APIs that prevent repeated two-slot switches. A separate implementation agent may carry out the approved mechanical/refactoring work. Both Avalonia and macOS are required surfaces; passing one desktop UI is not sufficient.

### C. Affinity display-unit policy

After sequential `ReportParameters` is stable, launch another agent to investigate display units independently of the model implementation.

The agent must compare:

- A unit chosen independently for each affinity value.
- One shared unit chosen for all affinities in a result or table.
- A user preference allowing both modes.

The investigation should favor per-affinity units if it integrates cleanly with existing formatting. A new preference should be proposed only if it has a clear presentation benefit and a contained implementation across Avalonia, macOS, web viewing, figures, and exports. No display-unit decision may change fitted values, `ReportParameters`, persistence shape, or the sequential model's acceptance criteria.

## File-Level Implementation

### 1. Indexed parameter infrastructure and model lifecycle

- Add `AnalysisITC.Core/Analysis2/ThermodynamicParameterSlots.cs`, an internal indexed mapping for the affinity, enthalpy, Gibbs, heat-capacity, entropy, and entropy-contribution families. Use it as the authoritative step-to-`ParameterType` mapping.
- Update `ParameterSet.cs` to append the step 3 and 4 enums and generate numbered labels, units, table headers, family membership, and subscript metadata generically.
- Update `ExperimentAttribute.cs` with `SequentialSiteCount`. Enforce the 2-4 integer range in the model/domain layer as well as in both desktop UIs.
- Introduce one authoritative model initialization sequence:
  1. Initialize default model options.
  2. Overlay stored/shared options.
  3. Validate options.
  4. Initialize the parameter table from the effective options.
  5. Overlay persisted or user parameter values.
- Route `AnalysisBuilder`, legacy `ModelFactory`, FTXTC, FTITC, bootstrap snapshot restoration, result updating, and relevant synthetic-clone construction through that lifecycle.
- Keep `ApplyModelOptions` idempotent and safe during loss evaluation. It must not rebuild the dynamic parameter table inside an optimizer iteration.

### 2. Sequential model kernel

- Add `AnalysisITC.Core/Analysis2/Models/SequentialBindingSites.cs`.
- Implement the binding polynomial, log-sum-exp state calculation, scale-aware mass-balance bisection, heat-content calculation, injection-heat evaluation, bootstrap solution data, and synthetic cloning.
- Initialize `Affinity_i` and `Enthalpy_i` for every active step plus `Offset`; expose no N-values and no syringe-activity option.
- Produce UI rows, bootstrap errors, derived report parameters, and temperature-dependence dependencies by iterating the active slots.
- Guess step enthalpies from the experiment's initial molar heat. Distribute initial affinity guesses logarithmically across the sampled concentration window. Persisted or user-edited values override guesses.
- Keep the initial-guess policy simple in the first implementation. Add more elaborate multistart logic only if the synthetic and published-data tests demonstrate that it is necessary.
- Activate the model in `AnalysisModelAttribute.GetAll()` only after construction, persistence, result, and both desktop UI paths are ready.

### 3. Single/global construction and constraint presentation

- Add real sequential dispatch to `AnalysisBuilder`, `ModelFactory`, `SolutionInterface.FromModel`, and the FTXTC persistence registry. Remove every sequential fall-through to `Dissociation`.
- Apply and validate the shared site count before initializing every member of a global model.
- Reducing the count must remove inactive step parameter overrides, global coordinates, and constraints. Increasing it must create fresh defaults for the newly active steps; values discarded by a prior reduction are not silently resurrected.
- For a sequential global analysis, expose only one affinity-family constraint selector and one enthalpy-family constraint selector, plus the existing offset constraint control if applicable.
- The selected family constraint style applies atomically to all active steps:
  - With four sites, the UI exposes four affinity values and four enthalpy values, but only one affinity constraint-style control and one enthalpy constraint-style control.
  - `None` keeps every step member-specific.
  - Affinity `SameForAll` creates one shared `Affinity_i` value per step across experiments.
  - Affinity `TemperatureDependent` creates one shared `Gibbs_i` value per step and evaluates member-specific `Affinity_i` values from temperature.
  - Enthalpy `SameForAll` creates one shared `Enthalpy_i` value per step across experiments.
  - Enthalpy `TemperatureDependent` creates step-specific shared reference enthalpy and heat-capacity coordinates and evaluates \(\Delta H_i(T)=\Delta H_{i,\mathrm{ref}}+\Delta C_{p,i}(T-T_\mathrm{ref})\).
- Persisted sequential constraint state must be family-consistent. Strict loading rejects inconsistent active-step styles; recovery skips the affected solution with a warning rather than guessing intent.
- Initialize each shared step coordinate from the corresponding already-initialized member parameter, not from a generic first-step guess.
- Make missing syringe-correction options safe with `TryGetValue`; sequential models do not define `UseSyringeActiveFraction`.

### 4. Persistence and bootstrap restoration

- Add stable FTXTC wire IDs for every new parameter and for `sequential-site-count`.
- Update FTXTC and FTITC restoration so effective model options are available before dynamic parameter initialization.
- Update bootstrap snapshot restoration and legacy bootstrap parameter restoration to reconstruct the correct concrete model and site count before parameters are installed.
- Validate the persisted shape for every primary and bootstrap solution:
  - Exactly one active affinity/enthalpy pair per configured step.
  - Exactly one offset.
  - No N-values.
  - No out-of-range step parameters.
  - Reported derived parameters consistent with the active steps.
- Apply equivalent validation to global members, global coordinates, and family constraints.
- Strict loading rejects malformed shapes. FTXTC recovery mode skips the solution or bootstrap component with a specific warning. No path may reinterpret sequential data as dissociation.
- Preserve existing `AnalysisModel` ordinals and append new `ParameterType` and `AttributeKey` members only.

### 5. Results and advanced-analysis capability

- Generalize active-step iteration in `AnalysisResult`, `AnalysisResultParameterEvaluation`, `BootstrapCorrelationAnalyzer`, result tables, figures, and all additional files found by Workstream B.
- Correlation matrices must contain the active fitted coordinates for a single fit, or the appropriate shared and selected-member coordinates for a global fit. Label sequential affinity coordinates `log10 Ka1` through `log10 Ka4` and omit N coordinates.
- Keep ordinary thermodynamic summaries and temperature-dependent evaluations/plots available for every sequential step.
- Keep the Spolar Record method strictly One-Set-Of-Sites-only. Do not construct it for a sequential result even when the series spans enough temperatures.
- Protonation and electrostatics advanced analyses also remain One-Set-Of-Sites-only for this implementation.
- Add explicit capability and unavailability-reason properties so clients can distinguish “the data do not qualify” from “the selected model does not define this analysis.” Do not disable ordinary temperature result presentation merely to suppress Spolar Record method construction.

### 6. Viewer, Avalonia, macOS, and export surfaces

- Update `ViewerDocumentReader` and the web viewer to render site count, step 3/4 thermodynamics, temperature evaluations, bootstrap confidence data, and correlations dynamically.
- Generalize parameter and constraint rows in Avalonia and macOS using the slot mapping and family-level constraint presentation.
- The site-count editor in both desktop applications must accept only integers 2-4 and rebuild the parameter/value UI and family constraint mapping immediately.
- Update result parameter graphs, final figures, temperature-dependence graphs, accessibility text, PDF metadata, clipboard export, table export, and other surfaces identified by Workstream B.
- Verify both Avalonia and macOS behavior explicitly. A platform-specific static list must not silently omit steps 3 or 4.
- Keep affinity display-unit changes out of this phase until Workstream C reports. Integrate its approved solution as a separate presentation-focused change.

### 7. Documentation and benchmark classification

- Extend `Documentation/UserManual/pages/06-fitting-models.md` with the state equations, macroscopic-constant interpretation, fixed integral count, absence of N, identifiability warning, and cell-macromolecule orientation.
- Extend the multiple-experiment, results, export, and reference pages with shared count, family-level constraint styles, per-step values, dynamic columns, and advanced-analysis unavailability.
- Document the new stable IDs and the chosen sequential model-schema compatibility rule in `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md`.
- Update the benchmark documentation to distinguish:
  - The six eLife WT Mn2+/Cd2+ runs as positive two-step sequential validations.
  - The SEDPHAT fixture as diagnostic-only because its orientation is outside the chosen sequential model scope.
  - The repository-local CBS supplementary dataset as an independent two-event/fractional-stoichiometry fit, not a sequential benchmark.
- Merge existing uncommitted documentation and viewer-test changes selectively; do not overwrite them.

## Test Plan and Acceptance Criteria

### Scientific kernel

- Add `SequentialBindingSitesModelTests.cs` covering:
  - Mass-balance residual and normalized state fractions for counts 2, 3, and 4.
  - Monotonic occupancy and a bracketed solution over randomized positive, finite parameter sets spanning the supported affinity and concentration ranges.
  - Finite behavior at zero ligand, saturation, affinity bounds, mixed-sign enthalpies, excluded injections, and segment starts.
  - Verification that the scale-aware stop meets the stated residual target and does not measurably change predicted heats when tightened further.
  - For \(n=2,3,4\), equivalence to `OneSetOfSites` with \(N=n\) for identical microscopic sites, using macroscopic \(K_i=((n-i+1)/i)K^0\) and equal step enthalpies.
  - Exact agreement with the existing offset convention.
  - Synthetic two-step recovery with both optimizers; 3/4-step forward and partially locked recovery cases without claiming identifiability unsupported by the generated data.

### Global constraints

- Add tests proving that a sequential analysis exposes one affinity-family style and one enthalpy-family style regardless of whether 2, 3, or 4 steps are selected.
- Prove that applying a family style updates every active step but preserves a distinct shared coordinate for each step.
- Test affinity `SameForAll` as shared `log10(Ka_i)`/Kd and affinity `TemperatureDependent` as shared \(\Delta G_i\) producing temperature-specific Kd.
- Test shared count propagation, 4-to-2-to-4 cleanup, fresh defaults after re-expansion, member-specific offsets, and step 3/4 mapping.
- Add regression coverage for existing one-set and two-set global constraint behavior based on Workstream A's approved plan.

### Bootstrap, persistence, and correlation

- Residual bootstrap and leave-one-out must restore the concrete sequential model and exact count.
- Require finite uncertainties and exact active-coordinate matrix dimensions and labels for counts 2 and 4.
- Add FTXTC and FTITC single/global round trips for counts 2-4, including bootstrap snapshots, correlations, exports, and viewer DTOs.
- Test the selected sequential model-schema compatibility rule and confirm enum ordinals remain unchanged.
- Test malformed count, fitted shape, reported shape, global coordinate, and family-constraint payloads in strict and recovery modes.

### Desktop and viewer surfaces

- Avalonia tests must cover site-count validation, immediate row rebuilding, family-level constraint controls, step 3/4 result graphs, correlations, exports, and accessibility labels.
- macOS must receive equivalent implementation and build/behavior coverage for parameter rows, constraint controls, figures, thermodynamic plots, and temperature plots.
- Web/viewer tests must cover dynamic step rendering, confidence data, correlations, and model count.
- Add a repository audit test or documented `rg` check ensuring newly supported surfaces do not retain unreviewed two-slot-only lists.

### Published-data validation

- Replace the independent-site interpretation in `PublishedElifeTwoSiteSourceDataTests` with sequential runs using all six WT fixtures, fixed count 2, excluded first injection, unweighted fitting, zero locked offset, and MicroCal dilution for source comparison.
- Require LM and Nelder-Mead convergence, finite interior parameters, the expected source-specific `Kd1 < Kd2` ordering, and clearly defined optimizer agreement criteria.
- Compare the Mn and Cd triplicate results with the published target ranges already selected for this benchmark.
- Add global fits of each metal's triplicate set using the sequential family constraints.
- Keep exponential dilution as convergence and finite-value coverage, not as an Origin parameter-truth assertion.

### Verification order

Run targeted tests during each phase, then finish with:

1. Targeted Core sequential, global-constraint, persistence, bootstrap, and benchmark tests.
2. Targeted Avalonia and Web tests.
3. A macOS project build and any available platform-independent macOS presentation tests.
4. `dotnet test AnalysisITC.sln` as the final regression gate.

## Delivery Sequence

1. Record baseline tests and preserve the existing dirty worktree.
2. Run Workstream A and review its global affinity semantics plan.
3. Run Workstream B and review its two-slot audit and refactoring boundaries.
4. Implement indexed parameter infrastructure, lifecycle, and the scientific kernel while the model remains unavailable in the UI.
5. Implement and verify persistence and bootstrap restoration, including the model-schema decision.
6. Land the approved global affinity prerequisite and sequential family-level constraints.
7. Implement Core result and uncertainty surfaces.
8. Implement the audited Avalonia, macOS, web viewer, export, and figure work.
9. Run Workstream C and land affinity display-unit behavior separately if approved.
10. Complete documentation and published-data validation.
11. Activate the model and run the final cross-platform regression gates.

## Definition of Done

The work is complete only when counts 2-4 fit and round-trip correctly; global analyses expose family-level constraint styles with correct per-step semantics; bootstrap and correlation shapes are exact; the Spolar Record method and other unsupported advanced analyses are not constructed; ordinary thermodynamic temperature presentation remains available; and Core, Avalonia, macOS, Web, export, viewer, and documentation surfaces all represent the active steps without silent truncation.
