# Approved Null model corrections (ITC-075)

Revision 3. Changes from revision 2 are listed at the end.

## Terminology and shared presentation

Null means the **no-binding model role**. Offset remains the name of the current constant dilution-heat implementation and its parameter. One analysis result uses the same Null model for all members; independently fitted members may have different parameter values.

User-facing text says **model**, never "flavor". Code and this plan use **Null model identity** for the saved `NullModelId`.

**N02 — Shared presenter**

- Derive the displayed Null model from saved `NullModelId`, replacing unconditional Offset labels.
- Use **`Null (Offset)`** for the current model, and the same pattern for other models.
- Preserve failure and per-experiment qualifiers. Use **`Null (not calculated)`** when no comparison exists and **`Null (model unknown)`** when identity is missing or unrecognized.
- Change the assessment explanation from "relative to Offset" to **"relative to the Null model used for the comparison"**.
- Use **"The Null model fit converged."** and the generic failure fallback **"The Null model fit failed."** Preserve detailed saved failure reasons.
- Change the RMSD explanation to **"Saved unweighted RMSD from the Null model fit."**

**N03 — Avalonia and native macOS result views**

- Replace both hardcoded collection labels with the shared formatter: **`Null (Offset), per experiment`**, using the actual model.
- Replace both empty-selection hints with the shared text **"Select an experiment to inspect its saved Null model comparison."**

## Reports, exports, and AI evidence

**N04–N05 — Reports and result exports**

- Change **"Offset prediction"** to **"Null prediction"**, retaining the selected unit.
- Change general comparison descriptions to **"Saved binding and Null model comparison."**
- Replace **"Null offsets"** headings, including CSV/TSV headers, with **"Null model parameters"**.
- Replace scalar-only summaries with named parameters from the matching saved `NullSolutions`, using reported values and appropriate units (`FloatWithError`/`Energy` formatting).
- Cell format names the parameter: `Exp1 (local): Offset = -1.23 kJ/mol`; multiple parameters are separated by `, ` within a member, members by `; ` as today. This intentionally changes the existing cell text.
- Preserve **Offset** as the actual parameter name. Permit the legacy `member.Offset` fallback only for confirmed Offset evidence; do not invent parameters for another model.
- Change pooled diagnostic text to **"Null model fit:"** and **"Null model AICc unavailable."** Preserve detailed saved reasons and numerical values.
- Update the corresponding entries in `Documentation/ANALYSIS_REPORT_MAP.md`.

**N06 — AI interpretation package**

- Replace the Offset-specific instruction with **"Use its saved Null model predictions and observations as the null-model evidence."**
- Add actual Null identity and generic named parameter evidence to both independent-member assessment records and Null-member records.
- Add `NullModelId`, `NullModel`, and `NullParameters`, with parameters represented using the existing `InterpretationParameterEvidence` structure.
- Populate parameter values, units, and constraints from the Null solution itself. Populate uncertainty **only where it was computed**; never emit a zero or placeholder uncertainty. Restored Null solutions are rebuilt from saved points and normally carry none.
- Retain `OffsetJoulesPerMole` for compatibility, but populate it only for a confirmed Offset Null model.
- Use the shared role-and-model label for the existing top-level `NullModel` field.
- Bump `PackageSchemaVersion` to **2.2** and add it to `SupportedPackageSchemaVersions` (keeping 2.0 and 2.1). Update `Documentation/Interpretation/MIST_RELAY_CONTRACT.md` and the Web endpoint tests that assert the current/supported versions. Confirm the compact model-input path carries the new fields.
- **Deployment dependency:** the MIST server must accept 2.2 before a client that sends 2.2 is released. Record this in ITC-075; no deployment is done in this pass.

**N07 — Current scientific guidance**

In scientific guidance **3.7.3** and summary guidance **2.2**, replace "Call analyses results; reserve model for the binding model." with:

> Call analyses results. Model can refer to a binding model or a Null model.

Leave older selectable variants and archived revisions unchanged. (3.7.3-compact does not contain the restriction.)

## Core identity and publication figures

**N08 — Comparison evidence class**

- Change the class description to **"Saved evidence from comparing a binding fit with its Null model fit."**
- Change the `NullSolutions` description to **"Ordinary Null model solution objects used for evaluation and presentation."**
- Remove the implicit `NullModelId = "offset"` default. Use an empty identity until a creation or restoration path explicitly assigns the actual model.
- Keep the existing `member.Offset` property and native data shape.

**N08a — Save-time identity guard (new)**

An empty identity would otherwise be written as `""`, which the reader rejects, so the whole comparison is silently discarded on reopen.

- `CaptureNullComparison` treats an empty `NullModelId` as an error. The existing serialization fallback then saves a status-only record (`NullFitSucceeded = false`, reason "Saved Null model identity was missing.") instead of an unreadable record.
- The fallback identity check changes from `?? "offset"` to an empty-or-null check, so the status-only record stays readable under the current Offset-only schema. This is the only writer change; it does not expand the schema.
- Round-trip tests cover every creation path: calculator pooled, calculator independent, and reopen → save → reopen for both. A test also confirms a deliberately empty identity produces the status-only record, not a discarded comparison.

**N09 — Comparison calculator**

- Change general summary, failure, convergence, and AICc messages to Null terminology.
- Retain Offset-specific parameter, equation, identifiability, and bounds diagnostics.
- Add virtual `Model.IsNullModel`, defaulting to false; Offset overrides it to true.
- Replace Offset-type checks that suppress a further binding-versus-Null comparison with the explicit Null-role check.
- Keep the current Offset fitting strategy and explicitly assign its identity.

**N12–N13 — Restoration**

- Change "saved local Offset snapshots" to **"saved local Null model snapshots"**.
- Change "pooled Offset comparison" to **"pooled Null model comparison"**.
- In both independent-member reconstruction paths, explicitly copy the applicable saved comparison identity.
- Where identity comes solely from the existing Offset-only legacy format, assign `"offset"` explicitly.
- Retain Offset-specific validation and restoration logic.

**N14 — Publication figures (simplified)**

When a member's binding output is suppressed, the figure draws the **saved Null solution** through the ordinary solution drawer. It no longer rebuilds an Offset from `member.Offset`.

- Resolve the Null solution from `comparison.NullSolutions` by experiment ID and by `NullModelId` matching the solution's registered model identity (`FTXTCPersistenceRegistry`). Exactly one match is required.
- Draw that solution as-is. Do not refit, rebuild, mutate attached experiments or saved solutions, or substitute binding parameters.
- Require the saved solution's injection IDs to match the experiment's current injections; otherwise treat it as unavailable.
- Remove `CreateNullDisplaySolution`'s scalar Offset reconstruction. No `CreatePresentationModel` hook is added.
- Missing, mismatched, or ambiguous solutions display **`Null (Offset) fit unavailable`** (actual model name, or `Null (model unknown) fit unavailable`).
- **Offset-correction toggle:** Null figures ignore `DrawFitOffsetCorrected` and always draw uncorrected integrated heats with the Null model curve. Residuals are observed minus Null prediction. Processing-derived error bars are unchanged. Binding figures keep the toggle as today.
- Show **`Null (Offset)`**, or the actual model, in solution information when the displayed solution is the Null model. Keep binding-model names when the displayed solution is binding.
- Leave intrinsic model names and internal solution identifiers unchanged.
- Update generic figure comments and the report map.

This changes current Null figure output when the offset-correction toggle is on (previously: data minus the Null offset against a zero line).

## Manual and help wording

**N01 — Manual**

Replace the constant-background definition with:

> For a titration, the null hypothesis is that the measured heats can be explained without binding. A Null model represents that no-binding explanation, including heat from dilution and mixing.

Introduce the implementation with:

> The comparison uses the Offset Null model, which predicts each injection heat as …

retaining its equation and units.

- Replace the scalar "Null model value" description with **"the Null model is fitted separately for each experiment"**.
- Change the figure description to **"Standard member figures show the fitted Null model against the uncorrected integrated heats."**
- Preserve surrounding report-suppression rules and independently changed text.

**N01 — In-app help**

- General help describes **"a Null model, which describes heat without a binding contribution."**
- Science help begins **"The comparison uses the Offset Null model, which describes heat without binding. It predicts…"**, followed by the existing equation and units.
- Preserve assessment thresholds and usage guidance.

**N01 — Native-format documentation**

- Change conceptual "member binding and Offset criteria" wording to **binding and Null model criteria**.
- Change the general assessment explanation from "over Offset" to **"over the Null model used for the comparison"**.
- Preserve accurate Offset-specific encoding descriptions.

## Tests, tracking, and deferred work

**N15 — Validation changes**

- Update presentation, report, export, AI, figure, and desktop assertions for the approved text and behavior.
- Retain explicit Offset mathematics tests.
- Add focused coverage for model labels (including `Null (model unknown)`), absent/failed comparisons, named parameter evidence and units, uncertainty omitted when not computed, guarded AI Offset fields, schema 2.2 acceptance, explicit restored identity, the save-time identity guard (N08a), and the Null-role comparison exclusion.
- Figure tests: a test-only varying model that reports `ModelType = Offset` but returns non-constant predictions verifies the drawer uses the saved solution rather than a rebuilt constant. Also cover identity mismatch, injection mismatch, ambiguous matches, the ignored correction toggle, and unchanged binding labels. No production enum value is added. Use independently specified expected values; these are presentation-contract tests, not forward-model validation.
- Run Core tests, Web tests (schema version), and relevant Avalonia/macOS tests and layout checks. Verify legacy Offset `.ftxtc` round trips.

Update **ITC-075** with these approved decisions, the MIST 2.2 deployment dependency, and the remaining deferred findings. Keep it open. Retain the Null-role guidance already added to AGENTS.md and CLAUDE.md.

**Unchanged in this pass:** native schema expansion, Offset-only native restoration, `member.Offset`, actual Null fitting-strategy generalization, new linear/exponential heat laws, model-selection controls, binding-model background composition, presentation rebuild hooks for non-Offset models, and broader correction of live graphs, exports, or bootstrap evaluation (A02). The writer changes only as described in N08a. Preserve concentration-bookkeeping terminology, assessment cutoffs, scientific equations, app versions, and unrelated working-tree changes.

## Changes from revision 2

- "Flavor" removed from all user-facing text; `Null (unknown flavor)` → `Null (model unknown)`; manual/help say "the Offset Null model" instead of "for example".
- N08a added: empty identity can no longer silently discard saved comparisons.
- N06: uncertainty only where computed; package schema 2.2 with relay-contract update and deployment dependency.
- N04–N05: export cell format fixed to name the parameter.
- N14: draws the saved Null solution directly; hook, scalar rebuild, and per-injection correction removed; Null figures ignore the offset-correction toggle.
- N15: test-only model needs no enum value.
