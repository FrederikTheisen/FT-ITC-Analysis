# External integrated-heat validation results — 13 September 2026

**FT-ITC recovered the generating parameters within the predeclared 2%
criterion for the native pytc one-site, two-independent-site, competitive and
two-step sequential examples. The native three- and four-step examples missed
that criterion.** The complete study ran 11 forward comparisons and 44 fits:
all 11 heat comparisons passed, 36 fits met parameter acceptance, and eight
fits missed it. All 44 optimizations reported successful termination.

The external generator uses pinned pytc code and exports integrated injection
heats. FT-ITC imports the `.DH` files, evaluates its models and fits the heats
without any baseline calculation or peak integration. These are synthetic
cross-software comparisons, not additional experimental benchmarks.

## Measured agreement

Each row contains four fits: two predefined starts with Levenberg–Marquardt
and two with Nelder–Mead. Errors below are the largest observed across those
fits and applicable parameters. Forward heat error is the maximum absolute
injection discrepancy divided by the largest absolute injection heat, evaluated
at the generating parameters before fitting. Ka is the association constant;
affinity error is calculated in Ka, not as a relative error in log10 Ka.

| Dataset | Pytc subshots per injection | Maximum heat discrepancy | Maximum Ka error | Maximum ΔH error | Fits within acceptance |
| --- | ---: | ---: | ---: | ---: | ---: |
| one-site-exothermic | 1 | 0.37072% | 0.25750% | 0.00020% | 4/4 |
| one-site-endothermic | 1 | 0.16792% | 0.25626% | 0.00020% | 4/4 |
| two-independent-sites | 1 | 0.26205% | 0.26094% | 0.00680% | 4/4 |
| competitive | 1 | 0.13943% | 0.02425% | 0.12825% | 4/4 |
| sequential-2 | 1 | 0.26194% | 1.47514% | 0.21236% | 4/4 |
| sequential-3 | 1 | 0.18409% | 3.29343% | 0.60955% | 0/4 |
| sequential-4 | 1 | 0.18443% | 4.75694% | 1.25472% | 0/4 |
| sequential-3-subshots-10 | 10 | 0.01389% | 0.20415% | 0.02738% | 4/4 |
| sequential-3-subshots-100 | 100 | 0.01967% | 0.08756% | 0.02723% | 4/4 |
| sequential-4-subshots-10 | 10 | 0.01386% | 0.28085% | 0.05411% | 4/4 |
| sequential-4-subshots-100 | 100 | 0.01964% | 0.12966% | 0.05553% | 4/4 |

All fitted N values met the 2% criterion; the largest N error was approximately
0.3775% in the competitive example. All enthalpies met 2%, and all fitted
offsets met the absolute 50 J/mol criterion. The eight misses were specifically
the higher-step affinity comparisons. Exact starting/fitted parameters,
parameter errors, offsets and unweighted heat RMSDs are in
[results.json](results.json); [results-table.md](results-table.md) is generated
directly from that record.

## What the refinement shows

pytc and FT-ITC use different approximations for displacement during an
injection. With each native 1 µL pytc shot, heat predictions already agree
within about 0.2% for the higher-step examples, but the fitted affinities shift
by 3.3–4.8%. Both solvers and both starts reach almost identical results.
This demonstrates why agreement of fitted curves or optimizer convergence alone
does not establish parameter recovery.

The four companion files simulate each physical injection as 10 or 100
successive pytc deliveries and sum their heats. Every original 1 µL FT-ITC
injection, total delivered volume, concentration, thermodynamic target,
fitting start and acceptance threshold is retained. No FT-ITC model or
production calculation was changed for this comparison. At 100 subshots,
the maximum affinity errors fall to **0.0876% for three steps and 0.1297% for
four steps**.

This is strong evidence that injection discretization contributes to the native
cross-software parameter discrepancy. It does not prove identical equations
or rule out every other implementation issue. The small remaining heat error
is not monotonically reduced between 10 and 100 subshots: FT-ITC retains its
own finite-injection approximation. Both the original misses and the refinement
results remain part of the evidence.

## Regression and study status

Normal regression runs execute 11 forward comparisons and 36 accepted recovery
cases. The eight strict native higher-step recovery attempts are an explicitly
skipped diagnostic theory by default. They remain executable by setting
`FTITC_RUN_EXTERNAL_PROTOCOL_DIAGNOSTICS=1`; the complete study then reports
**47 passed and 8 failed**, with no skipped cases. Those failures are not counted
as successful scientific validation, and their 2% threshold was not relaxed.

The full ordinary Core suite then passed: **1,140 passed, 0 failed, one diagnostic
theory skipped** (that theory contains the eight opt-in attempts). All eleven
`.DH` files and `reference.json` reproduced byte-for-byte in a second generation
run. Verification used the working tree based on
`111438cccc69f7271f4fec70b21ee1729b65d59a`, macOS arm64, .NET SDK 10.0.302,
Release configuration and restored dependencies. See [verification.json](verification.json)
for source, fixture and test-artifact hashes. No application production code
was changed for this study; existing uncommitted changes predate it.

The [reproduction instructions](README.md#reproduction) describe the exact
generator and test commands. The archived result summary requires all 55
comparison records and refuses to summarize a partial selection. The previous
native-protocol run also contained all seven original datasets; no unsuccessful
example was removed to improve the reported recovery rate.

## Limits and publication claim

A supported claim is: **independently generated integrated-heat examples recover
known thermodynamic parameters under the specified model mappings and numerical
settings, with documented injection-convention sensitivity.** The first
one-site examples are exothermic/endothermic; the two-class example uses one
site of each type; competitor properties are supplied; sequential tests cover
2–4 macroscopic steps with every fitted coordinate free, including offset.

The set does not establish recovery from arbitrary starting points, performance
under default solver stopping settings, empirical model adequacy, noisy-data
identifiability, calibrated uncertainty, global fitting, or a matching external
monomer–dimer benchmark. These limits remain relevant to the publication audit.
