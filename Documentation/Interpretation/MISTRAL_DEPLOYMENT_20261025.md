# Mistral deployment record — 25 October 2026

Mistral is **enabled on MIST alongside OpenAI**. The key and EU catalogue are
verified, all four approved paid smoke requests passed, and public options
advertise both providers. The combined recorded cost was USD 0.00518746.
The provider disclosure is published at `https://ft-itc.org/privacy` and MIST
reports consent version `ft-itc-privacy-1.1`. The published notice includes the
approved no-training statement, following the user's confirmation that training
is disabled for this Mistral API workspace.

## Installed release

- Release identity: `20261025-164041-mistral`, based on `9e9f936e` with the scoped
  provider changes. Application version metadata was unchanged.
- Web assembly SHA-256:
  `485ba6f37d9c7a50dce8be53bc997097cf4329af618bcfcc9d6f577d4220d315`.
- Active Web: `/opt/ftitc-web`; matching status-email runtime:
  `/opt/ftitc-status-email`.
- Protected deployment state, backups, compatibility evidence and smoke records:
  `/var/lib/ftitc-deploy/20261025-164041-mistral/`.
- Source was staged separately from the working checkout. Unrelated
  `CITATION.cff` edits were excluded and preserved. No commits, pushes, packages,
  signing operations or application version changes were performed.

## Accounting and operational verification

Generation was paused and drained for consistent SQLite/configuration/executable
backups. Migration on copies preserved all original rows across 15 historical
tables, 232 duplicate-request claims and balances for 18 accounts. The previous
executable admitted two invented requests on the migrated rehearsal ledger, which
the candidate then read correctly. A synthetic Mistral receipt and duplicate
claim survived candidate → compatible recovery → candidate round trips without
any provider dispatch.

Recovery uses the compatible runtime with Mistral disabled, preserving the entire
current ledger. It does not restore an older database over subsequent charges.
Production historical rows and shared preset properties were compared after
activation. The registry was upgraded to schema 12 while preserving MIST's custom
OpenAI models, reasoning settings, descriptions, access tiers, quotas and size
limits. Its file remains `root:ftitc-web`, mode `0640`.

Internal health is healthy; public status advertises relay v7 and remains
available. Public access to `/api/internal/health` returns HTTP 404. The matching
status-email runtime successfully rendered a preview; no test email was sent.
Administrator preset listing/editing and provider-filtered usage/export commands
were exercised. The existing daily probe remains on OpenAI.

Two unresolved administrator charges from cancelled September requests were
present before deployment and remain unchanged. None of the four new smoke requests left
an unresolved cost. No historical liability was waived or discarded.

## Live request budget and results

The conservative initial estimate for all four tiny requests was USD 0.06275136,
including the proposed native Mistral alternative, below the USD 0.10 estimated
ceiling. All four authorized requests have now been dispatched:

| Provider | Preset | Effective model | Reasoning | Recorded USD | Retrieval |
|---|---|---|---|---:|---|
| OpenAI | Summary | gpt-6-luna | xhigh | 0.00034905 | disabled, zero calls |
| OpenAI | Fast | gpt-6-luna | medium | 0.00386095 | one file-search call |
| Mistral | Summary | zai-glm-5-3-flash | provider default | 0.00039578 | disabled, zero calls |
| Mistral | Fast | zai-glm-5-3-flash | provider default | 0.00058168 | disabled, zero calls |

**Recorded total: USD 0.00518746.** All four completed requests used invented evidence,
exactly one provider attempt and durable receipts with provider/model provenance
and generic response IDs. Costs were independently recomputed from fresh input,
cached input, cache-write, output and search quantities at the recorded rates.
Duplicate-request claims were retained. No retries or larger follow-up tests were
made.

The requests used the deployed executable in temporary loopback-only
services with a 2,048-token output cap and the production accounting path. This
left installed OpenAI limits and settings unchanged. Both temporary services were
stopped and their public credentials revoked after testing. **The four-request
authorization is exhausted. No further paid tests are authorized.**

## Validation and limits

- Core: 5,111 passed, one skipped.
- Web: 600 passed in the final isolated source, including both adapters, all
  configured Mistral preset mappings, reasoning omission/values, string/chunked
  output, truncation, cancellation, timeout, transport failures and durable costs.
  Republishing that final source reproduced the deployed Linux Web assembly
  SHA-256 exactly.
- Provider permissions and shared allowances were checked through real relay
  endpoints with fake transports for Public, Registered, Advanced and
  Administrator accounts.
- Relevant Avalonia tests: 65 passed.
- Native managed compilation and interpretation/provider/preferences/report
  harnesses passed. A stale enrollment harness expectation was aligned with the
  existing Core contract: credential enrollment notifies without reapplying
  Preferences.

Desktop checks are automated harness evidence, not manual cross-platform release
qualification. The smoke tests establish operation, not scientific equivalence,
interpretation quality or equivalent latency between models.

The Web viewer privacy text and public Mistral provider disclosure are deployed.
The website Worker and its secret bindings were preserved; only the privacy asset
changed. The published notice states that model training is disabled and retains
separate retention and regional-processing qualifications. No zero-retention
guarantee is asserted.

## Configuration and activation verification

The key was verified through a non-billable authenticated EU `/v1/models` read;
the protected file retains mode `0640`. The EU catalogue includes all preferred
models and native alternatives. The prepared five preset mappings are unchanged
from the approved candidates. Published rates still specify the 10% regional
premium, included in the configured rates.

The current [regional inference documentation](https://docs.mistral.ai/inference/regional-inference),
[reasoning documentation](https://docs.mistral.ai/studio/conversations/reasoning),
[GLM 5.3 Flash model card](https://docs.mistral.ai/models/zai-glm-5-3-flash),
[commercial terms](https://legal.mistral.ai/terms/commercial-terms-of-service/),
[additional third-party technology terms](https://legal.mistral.ai/terms/additional-terms/)
and [data-processing addendum](https://legal.mistral.ai/terms/data-processing-addendum/)
were reviewed. GLM is hosted by Mistral; FT-ITC makes no direct Z.ai API call.
Regional inference and training/retention controls remain distinct. The user
confirmed that training is disabled for this API workspace and approved the
published statement. That is user-confirmed
configuration, not an independent audit of account controls. ZDR enrollment is not
asserted. The unused Large 4 preview entry was removed from source and the active
catalogue because preview terms differ.

The configuration activation completed successfully. Its state, guarded runner,
fresh consistent ledger/configuration backup and rollback configuration are under
`/var/lib/ftitc-deploy/20261025-164041-mistral/activation/`. Generation was paused
and drained, the service restarted with the key, and health verified before and
after resuming. Failure recovery preserves the complete current ledger.

Final checks confirm both providers in public options, shared Public-tier Summary
and Fast choices, resolved smoke accounting, correct provider-filtered usage and
exports, unchanged OpenAI settings/rates, internal health passing, public status
available, internal health still excluded publicly (404), and registration consent
version `ft-itc-privacy-1.1`. Administrator listing and status-email preview pass.
No further paid request was made during activation.

The approved notice is published at both `/privacy` and `/privacy.html`; both
live responses match the 14,524-byte source exactly. Its SHA-256 is
`9cc2261245161e64a6fa732f74400d1575f7821eea92ad1d2e52b4a59a61fcbc`.
Website deployment version: `99f4b170-9a5d-4231-97ff-50604388ac06`.
Only `/privacy.html` was uploaded; 93 assets were already present. The compiled
Worker SHA-256 remains
`9231ee1126d830048df5cda90e0ae557ed8957973dae11e4d0ee472fd1f00cc8`.
Its proxy secret binding, compatibility date and existing subdomain settings were
verified unchanged. Registration status still reports `ft-itc-privacy-1.1`, and
MIST public status remains available on relay v7. All 38 website Worker tests
passed.

The service itself is operational. The four-request authorization is exhausted;
all further verification must remain non-billable unless separately authorized.

## Separately authorized follow-up test

At 21:03 UTC on 25 October, the user authorized one additional tiny Mistral
Summary request with invented evidence, an estimated USD 0.01 ceiling and no
retry. Its conservative estimate was USD 0.00825, including the full configured
6,000-token output allowance; production limits were unchanged.

The request passed through the public MIST endpoint in 3.155 seconds, selecting
`zai-glm-5-3-flash` with provider-default reasoning. It returned a final synthetic
summary and recorded 2,052 input tokens, 344 output tokens and **USD 0.00052778**.
The single receipt has a provider response ID, zero retrieval calls, independently
verified cost and a retained duplicate-request claim. No new unresolved cost was
created. The temporary Public credential was revoked and final health was healthy.

Execution ID: `197d7c6f4bb8427ca0939b6eaea9d70e`. The protected record is
`/var/lib/ftitc-deploy/20261025-164041-mistral/followup-mistral-summary-20261025.json`.
The original four deployment tests remain recorded separately; all five requests
together cost USD 0.00571524. No retry or further paid request was made.

## JORS evaluation and output allowance — 26 October

A separately authorized evaluation used the saved JORS example report with
injection-level evidence. Fast (`zai-glm-5-3-flash`, provider-default reasoning)
failed to return an accepted interpretation with the 6,000-token output allowance.
Its receipt reports 6,000 output tokens, all reasoning, and USD 0.009709095.

The user authorized increasing Mistral's output allowance to 15,000 tokens and
retrying. The Mistral source default and active Web configuration now use 15,000;
OpenAI's allowance remains 6,000. The configuration-only activation used the
existing pause/drain procedure, consistent configuration/ledger backups and
health/accounting checks. No executable or application version was replaced.
The backup and guarded activation state are in
`/var/lib/ftitc-deploy/20261025-164041-mistral/token-budget-15000-20261026/`.
All 36 focused Mistral/provider-selection tests passed locally.

The authorized Fast retry also failed: 15,000 output tokens were reported, all
reasoning, with no accepted final text. Its independently verified charge was
USD 0.014659095, giving USD 0.02436819 across both JORS attempts. Both receipts are
known; no new unresolved charge was introduced. MIST remained healthy and the
temporary credentials were revoked. Fast's reasoning/output-budget behavior on
this realistic input remains an operational limitation; the tiny Summary tests
do not establish completion or scientific quality for this workload.

An administrator changed the live Default Mistral mapping to Medium 3.5 with
high reasoning; this change was preserved. The user approved the current presets
with a USD 0.70 cumulative ceiling, then explicitly authorized resuming Default
and Advanced after the repeated Fast failure. Both completed with 15,000-token
allowances and one attempt each: Medium 3.5/high in 49.309 seconds for
USD 0.127416300, and GLM 5.3/high in 21.838 seconds for USD 0.085954220.
Their effective-input and scientific-instruction fingerprints match. Retrieval
remained off and every new charge is fully accounted. Temporary test credentials
were revoked and final MIST health was healthy.

The complete four-request JORS comparison cost USD 0.237738710, including both
failed Fast attempts. Advanced supplied the stronger draft on this example,
while still making unsupported sensitivity claims. Default's completed text
incorrectly calls the positive entropy contribution favorable. Neither output
was approved into the saved project; these results do not establish a general
model ranking or scientific equivalence. No further Fast retry was made.
The [JORS evaluation record](../../Artifacts/Investigations/JORS-Mistral-Evaluation-2026-10-25/review.md)
contains the unedited completed outputs, scientific review, evidence fingerprints,
receipts and costs. The final protected comparison state is
`/var/lib/ftitc-deploy/20261025-164041-mistral/jors-evaluation-15000-remaining-20261026/evaluation.json`.

## Summary and Fast correction — 26 October, activated on MIST

The user clarified that Summary and Fast must use a fast model with limited
reasoning. GLM Flash with provider-default reasoning did not meet that intent on
JORS. The source seed mappings now use the plan's native alternative, Small 4
(`mistral-small-2603`) with explicit `none`, for both `summary` and `instant`.
Mistral documents `none` as minimal reasoning; it is not a guaranteed numerical
reasoning-token cap. The existing transport tests verify that the explicit
setting reaches the provider for both presets. All 36 focused provider tests pass.
The full Web suite also passes: 609 tests, zero failures.

Initial SSH attempts timed out while the VPN was unavailable. After connectivity
returned, Small 4 was confirmed in the EU `/v1/models` catalogue and both live
registry mappings were updated through the existing admin commands:

```bash
cd /opt/ftitc-web
sudo dotnet AnalysisITC.Web.dll generation-presets set summary mistral-small-2603 none --provider mistral
sudo dotnet AnalysisITC.Web.dll generation-presets set instant mistral-small-2603 none --provider mistral
```

Activation completed at 08:51 UTC (09:51 Europe/Paris), with registry revision
`20261026-085143105`. A semantic comparison verified that only the two Mistral
mappings and registry revision/timestamp changed. All OpenAI mappings, the other
Mistral presets, descriptions, permissions, allowances and guidance were retained.
Ownership and mode remain `root:ftitc-web`, `0640`. The installed executable and
application settings were unchanged; no restart was needed. The current Web
assembly SHA-256 is
`af735c85ef928ea00310b4bc051c4f4f2dffc49b25c3066f9d3db6489e215c47`,
reflecting a runtime already present before this configuration-only operation.

Admin listing verifies both mappings, and loopback health is healthy. Public
status at `https://app.ft-itc.org/api/interpretation/status` returns HTTP 200 and
reports available on relay v7; public internal health remains excluded (404).
No paid request was made for this correction. The pre-change registry backup and
verification record are stored in
`/var/lib/ftitc-deploy/summary-fast-minimal-reasoning-20261026T085142308965Z/`.

The user then requested one new Fast interpretation on the same frozen JORS
evidence. Small 4/`none` completed in 5.052 seconds (provider latency 4.785 seconds),
returning 178 words and reporting 49,192 input tokens, 343 output tokens and zero
reasoning tokens. Its durable charge is USD 0.008343060, independently verified
from the receipt. The effective-input, scientific-instruction and output-
instruction fingerprints match the earlier completed comparisons. Retrieval
remained off; there was one provider attempt and no new unresolved charge.
The temporary test credential was revoked and final internal health was healthy.

All five JORS attempts now total USD 0.246081770, below the existing USD 0.70
ceiling. The updated [local review](../../Artifacts/Investigations/JORS-Mistral-Evaluation-2026-10-25/review.md)
retains the unedited Fast output and notes its overconfident residual and
temperature-dependence claims. The protected request record is
`/var/lib/ftitc-deploy/20261025-164041-mistral/jors-fast-small4-20261026/evaluation.json`.
These observations establish completion and latency on this example; they do not
establish scientific equivalence or general performance. Summary was not retested.

## Summary and current standard follow-up — 26 October

The user subsequently requested Summary and the standard preset using their
current mappings. Registry revision `20261026-091735249` now mapped Summary and
Default to GLM Flash/provider-default, retained Small 4/none for Fast, and used
Compact 3.7.3 as the default scientific guidance. These intervening administrator
changes were preserved. The test interpreted `standard` as its literal registry
ID, displayed Advanced, mapping to GLM 5.3/high.

Summary completed in 26.597 seconds for USD 0.010152780, returning 404 words with
37,362 input tokens, 7,251 output tokens and 6,297 reasoning tokens. It used
ordinary Core summary output instructions and Summary guidance 2.2. The standard
request completed in 30.916 seconds for USD 0.089298880, returning 701 words with
39,016 input tokens, 6,036 output tokens and 4,840 reasoning tokens. It used
Compact 3.7.3 guidance. Both retained the frozen JORS model evidence.

The pair cost USD 0.099451660; all seven JORS requests now total USD 0.345533430,
below the existing USD 0.70 ceiling. Each had one provider attempt, a known
durable receipt, verified cost, zero retrieval calls and a retained duplicate
claim. Temporary test credentials were revoked; final internal health was healthy
and the two historical unresolved charges were unchanged. No configuration was
changed and no request was retried.

The [local follow-up review](../../Artifacts/Investigations/JORS-Mistral-Evaluation-2026-10-25/summary-standard-followup.md)
retains the outputs and limitations: Summary reverses the cell/syringe protein
assignments; standard still makes unsupported robustness assurances. Summary's
current provider-default reasoning remains substantial. The protected records are
`/var/lib/ftitc-deploy/20261025-164041-mistral/jors-summary-current-20261026/evaluation.json`
and
`/var/lib/ftitc-deploy/20261025-164041-mistral/jors-standard-current-20261026/evaluation.json`.
