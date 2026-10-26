# Mistral provider: implementation, MIST deployment and testing

Approved scope, 2026-10-25. Deployment and four tiny live tests are included.
Application version changes, release packaging, commits and pushes are excluded.

Implementation and MIST deployment status is recorded in
[the deployment record](../Documentation/Interpretation/MISTRAL_DEPLOYMENT_20261025.md).
Mistral is enabled, the key and EU configuration are verified, and all four
smoke requests passed. The published Mistral provider disclosure includes the
approved explicit no-training wording and retains separate retention
and regional-processing qualifications.

## Credentials and configuration

Install `Interpretation__Mistral__ApiKey` in MIST's existing protected
`/etc/ftitc-web/interpretation.env` using `sudoedit`, preserving existing entries
and permissions. Endpoint, models, reasoning choices, token limits, prices and
preset mappings stay in non-secret configuration shared with the admin tool.
Mistral starts disabled; installing a key alone does not expose it to users.

Use the EU regional chat-completions endpoint. Verify its model availability,
prices including the regional premium, applicable hosted-model terms and
account training/retention controls before enabling user access.

## Service, protocol and accounting

- Add Mistral alongside OpenAI using the existing scientific evidence and prompt
  paths. Mistral retrieval remains off; Summary retains its dedicated guidance.
- Relay v7 adds optional `provider`, defaulting to OpenAI. Versions 3–6 remain
  OpenAI. Options advertise configured providers and each administrator model's
  provider. Reject unknown/unavailable providers before admission and dispatch.
- Never switch providers automatically. Unconfigured Mistral leaves OpenAI usable.
- Schema 12 adds complete per-provider mappings while preserving customised
  OpenAI mappings and shared descriptions, tier access, quotas and size limits.
- All model IDs, reasoning choices, output limits and prices are configuration.
  Provider-default reasoning omits the API parameter and differs from `none`.
- Read string/chunked responses, exclude thinking chunks, reject length-truncated
  replies and retain existing cancellation, timeout and error behavior.
- Add provider identity and generic provider-response identifiers to durable
  accounting. Preserve legacy OpenAI identifiers, receipt identity, balances,
  reconciliation and shared allowances. Health identifies each configured
  provider; the daily probe remains OpenAI.

## Desktop and administration

- Native macOS and Avalonia generation dialogs and Preferences expose Provider.
  All access tiers receive it, retaining existing preset and allowance limits.
- Ordinary accounts choose provider/preset. Administrators choose provider/model
  and the model's supported reasoning settings. Share labels/tooltips in Core.
- Save one provider preference beside existing settings; OpenAI is the initial
  default. An unavailable saved provider produces an availability error.
- Existing saved provenance records provider/model/reasoning; no `.ftxtc` change.
- Admin preset editing/listing supports provider; the CLI's optional provider
  defaults to OpenAI. Usage views/exports identify and filter provider.
- Update relay/deployment documentation, privacy text and concise manual/help
  text. Retain shared preset descriptions; add no Mistral retrieval notice.

## Starting Mistral mappings

| Preset | Preferred candidate | Reasoning | Native alternative |
|---|---|---|---|
| Summary, Fast | GLM 5.3 Flash | provider default | Small 4, none |
| Default | Small 4 | high | same |
| Advanced | GLM 5.3 | high | Medium 3.5, none |
| Comprehensive | GLM 5.3 | max | Medium 3.5, high |

Confirm exact IDs and supported settings in the selected region. Use native
alternatives if GLM availability or applicable data handling is unsuitable.
Mappings are configurable starting counterparts, not established speed or
scientific-quality equivalents.

## Deployment and validation

1. Implement and run Core/Web suites, relevant Avalonia tests, native managed
   compilation and interpretation/preferences harnesses. Use fake transports
   for mappings, reasoning, truncation, errors, cancellation and shared quotas.
2. Stage an isolated release containing only approved changes. Preserve installed
   configuration, secrets, custom OpenAI settings and unrelated working-tree edits.
3. Follow the existing pause/drain, consistent SQLite/configuration backup,
   migration rehearsal and compatible-recovery procedure. Stage matching Web and
   status-email runtimes. Verify historical balances, unresolved liabilities and
   duplicate-request claims; recovery preserves the complete current ledger.
4. Deploy with Mistral disabled, verify OpenAI service operation, validate complete
   Mistral configuration, then enable Mistral.
5. Make four tiny invented-evidence requests total: Summary and Fast once per
   provider, with short output limits. Target pennies with a USD 0.10 estimated
   total ceiling. Estimate before dispatch and inspect actual recorded usage
   afterward. Stop on errors, unresolved costs or projected overrun; no automatic
   retries, larger requests or broader scientific comparison.
6. Check internal health, public status, exclusion of internal health from public
   access, admin operation and provider/model/accounting provenance. Disable
   Mistral if its checks fail; use rehearsed recovery for shared-service failures.
7. Report deployed identity, test results, recorded cost and remaining limitations.
   Smoke tests establish operation, not scientific equivalence.
