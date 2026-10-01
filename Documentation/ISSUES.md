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
