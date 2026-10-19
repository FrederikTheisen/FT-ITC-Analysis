# Derived-parameter uncertainty across all models

Status: Feature plan registered on 17 October 2026; design decisions remain open. Tracker: [ITC-117](ISSUES.md#itc-117---derived-parameter-uncertainty-across-all-models). Registration is not implementation approval.

This feature supersedes the narrow CR-029 implementation proposal. Treat competitive apparent affinity as one example of a broader question: how should FT-ITC calculate and present uncertainty for every derived parameter, consistently across models and uncertainty methods?

## Agreed requirements

- Consider all derived parameters for all implemented models: One Set of Sites, Two Sets of Sites, Competitive Binding, Sequential Binding Sites, Dissociation, and Offset in its Null-model role. Establish explicitly where a model has no relevant derived quantities.
- Include bootstrap, leave-one-out, and profile-likelihood uncertainty. Profile-likelihood handling is part of the scientific design, not a fallback to decide during implementation.
- Preserve the original best-fit central values. Retained refits determine uncertainty, not replacement means or medians.
- Zero error means the error does not exist. Do not invent additional error tracking, availability flags, or display markers for this feature.
- Keep native macOS, Avalonia, the web viewer, reports, and exports consistent wherever they expose the same quantity and evaluation context.
- This is planning work. Do not implement a competitive-only patch, adopt the previous narrow fallback rules as settled policy, or change scientific behavior while registering the feature.

## Current evidence and adjacent work

- [CR-029](CODE_REVIEW_2026-10.md#cr-029--competitive-apparent-kd-omits-fitted-stoichiometry-uncertainty): the initial free-competitor calculation reads scalar N, so apparent Kd omits uncertainty from its N dependence. The same competition factor supplies apparent Ka and apparent enthalpy.
- A controlled Core probe used 10 µM initial cell and competitor concentrations, competitor Ka = 10^6 M^-1, and fixed intrinsic Kd = 10 nM. Retained N values of 0.9 and 1.1 produce apparent Kd values of 41.6227766 and 33.1662479 nM, while the primary result reports 37.0156212 nM with zero SD. The existing center-preserving distribution convention gives approximately 4.24520645 nM dispersion. This establishes the missing contribution; it does not choose a universal propagation method.
- In the bundled competitive project, current reported SDs are larger than dispersions calculated from the saved refits. Missing dependence and correlations do not imply that uncertainty is always underestimated. Probe source and output: `/private/tmp/cr029-investigation.xNEcg7/`.
- Shared thermodynamic solution helpers, linked global evaluation, and summary evaluation already implement different transformations and aggregation contexts. Inventory these before proposing a shared interface or replacing calculations.
- [CR-024](CODE_REVIEW_2026-10.md#cr-024--exported-and-displayed-summary-kd-uncertainties-disagree) was resolved with a selected summary convention: propagate summarized Gibbs SD and transform its interval endpoints. Read `SUMMARY_SEM.md` and preserve that agreement unless a specific change is discussed and approved. A per-fit derived distribution and a collection summary are distinct estimands.
- [CR-010](CODE_REVIEW_2026-10.md#cr-010--failed-profile-diagnostics-can-still-supply-numerical-uncertainty) records the local fix preventing failed owning profile runs from supplying numerical intervals. Retain that protection. [ITC-114](ITC-114_PROFILE_PRIMARY_RECOVERY_PLAN.md) concerns primary-fit recovery and remains separate.

## Design stages

### 1. Inventory quantities, evidence, and consumers

Create a matrix listing each derived quantity, its formula and units, model/step applicability, source coordinates and external inputs, temperature, stored evidence, current propagation rule, and consuming surfaces. Distinguish fitted coordinates from transformed values, independent members from globally constrained values, and individual fits from collection summaries.

Cover association/dissociation constants, Gibbs energy, entropy contributions and entropy, competitive apparent affinity/enthalpy, corrected stoichiometry/activity outputs, and c-values. Trace temperature-dependent quantities and downstream advanced-analysis results that derive from model parameters; identify their existing independent plans and any shared uncertainty dependencies before deciding implementation boundaries. Check figure/envelope and clipboard consumers as well as tables and reports. This inventory is not authorization to redesign every advanced analysis.

### 2. Agree method-specific propagation

For retained-refit methods, decide which quantities use direct evaluation of each joint refit and which retain an explicitly justified summary transformation. Specify how sampled concentrations and model options, global/local coordinates, retained-refit selection, and saved historical evidence participate. Preserve the existing distribution-summary convention unless a change is separately justified and approved.

For profile likelihood, distinguish:

- A derived value that is a monotonic transformation of one profiled coordinate: determine when transforming the coordinate's interval endpoints is sufficient.
- A value depending on several fitted coordinates: marginal profile intervals do not provide a joint distribution. Compare profiling the derived quantity itself by constrained refitting with explicitly justified approximations; decide which quantities warrant either approach and the computational cost.
- Quantities using externally supplied uncertain inputs: determine whether those inputs participate in profiling, are held fixed, or require a separate approved propagation rule.
- Linked temperature-dependent quantities and collection summaries: define the owning coordinates and evaluation temperature, including shared constraints and covariance.

Specify SD versus confidence-interval meaning for each supported method. Do not describe a profile equivalent SD as bootstrap dispersion or manufacture joint samples by independently drawing marginal profile intervals. Where the approved method supplies no error, use zero error without adding tracking states. Existing profile-run diagnostics remain existing evidence, not a new derived-error tracking system.

### 3. Produce the implementation-ready plan

After the inventory and scientific choices are reviewed, define the minimum shared Core evaluation interface and its callers. Set explicit rules for fixed and zero-error inputs, algebraic cancellations, exact special cases, insufficient or invalid retained refits, incomplete/censored/failed profiles, and domain boundaries. Do not silently choose independent propagation or discard problematic refits as an implementation convenience.

Specify behavior after save/reopen and Update Result using saved inputs and temperature. Read the FTXTC specification before proposing persistence changes; prefer existing evidence, and bring any necessary schema change back for approval. Identify which current calculations, tests, documentation claims, and summary conventions will change and which will remain intact.

## Validation and completion criteria

- Derive independent mathematical expectations for every affected parameter family and model/step, including cases where covariance increases uncertainty, reduces it, or cancels it. Include CR-029's reproduction.
- Validate monotonic profile transformations independently; for multi-coordinate proposals use analytic or independently solved reference problems and assess interval interpretation, bounds, and incomplete profiles. Keep uncertainty validation distinct from forward-model and solver validation.
- Compare identical quantities and evaluation contexts across desktop results, report/figure output, clipboard, CSV/TSV, interpretation output, and the web viewer. Cover local, independent, shared-global, linked-temperature, and saved-result paths.
- Run the full Core suite and relevant desktop/web checks when implementation is approved. Document interactive versus automated verification precisely.
- Update user-facing manual/help and the report map only after the calculation and interpretation policies are agreed and implemented. Close CR-029 only after its regression is covered and the affected consumers are validated.

The next deliverable is the inventory and method-policy proposal for review, followed by a decision-complete implementation plan. No solver, parameter, serialization, or presentation changes are approved by this registration.
