# Reproduction

The native `.DH` fixtures and default pytc outputs remain in `PytcBookkeeping` and `TwoSiteStoichiometry`. The precision generator executes the same pinned external models and full finite-injection protocol. Its only C change is absolute root tolerance; competitive expressions run unchanged with mpmath arithmetic. Source, generator and compiled-extension hashes are recorded in `precision-reference.json`.

Use a clean pytc checkout at `d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac`, Python 3.9.6, numpy 1.26.4, scipy 1.13.1, mpmath 1.3.0 and the pytc 1.1.5 dependencies. From the repository root:

```sh
python AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/precision_reference.py --pytc-source /path/to/pinned-pytc
FTITC_PYTC_RESULTS=/tmp/forward-38 FTITC_TWO_SITE_RESULTS=/tmp/forward-14 dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~PytcExternalReferenceTests|FullyQualifiedName~TwoSiteStoichiometryForwardTests' --logger 'trx;LogFileName=forward.trx' --results-directory /tmp/forward-tests
python AnalysisITC.Tests/ScientificValidation/ForwardPrecision/report_results.py --original-results /tmp/forward-38/comparisons.json --stoichiometry-results /tmp/forward-14/comparisons.json --trx /tmp/forward-tests/forward.trx
```

The report requires all 52 comparisons and the reference convergence check to pass the unchanged floating-point criterion. It plots recorded predictions and reference heats; it does not generate model predictions or fit parameters.
