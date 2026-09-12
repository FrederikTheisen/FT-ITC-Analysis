# MIST deployment notes

Deploy the Web project and its dependencies using the existing MIST procedure.
Stage the new service beside the current service, preserving the protected
credential environment, retrieval configuration, and hosted viewer files.
Keep a complete rollback copy of the prior deployment until production health
and a synthetic request have passed. The A01 accounting change also migrates the
usage database; deployment must follow the accounting migration procedure below.

Before activation, run the Web tests and verify that a synthetic package using
the 5.0 envelope returns a response with the evidence schema, guidance
revision, instruction fingerprints, retrieval provenance, and effective-input
fingerprint. Use invented evidence only; never upload real experimental data
for deployment testing. Verify health and the interpretation status endpoint
after activation. Confirm that malformed JSON, missing envelope fields,
unsupported evidence schemas, invalid task types, and oversized bodies are
rejected before a model call. Exercise both the normal interpretation and
summary task with invented evidence; summary must not invoke retrieval or
consume capability-code quota.

The service keeps its existing model, retrieval, pricing, quota exemptions, and
context-size fallback choices. Each fallback now requires durable, resolved
accounting for the previous attempt. The configured provider timeout covers the whole generation,
including response-body reads and fallback attempts. Before activation, verify
with a controlled provider that a client disconnect propagates through the
deployment's reverse proxy and cancels the provider request without starting a
fallback. In-process cancellation tests do not verify that proxy behavior.
Do not add a formatting registry or
negotiation layer. Scientific instruction resources are retained by revision
in server source. Their fingerprints identify the exact text used but cannot
reconstruct it. A rollback must preserve accounting written after migration;
restoring an old database or an executable that overwrites charges is not a safe
rollback. Keep generation paused until the chosen executable can read and preserve
the complete current ledger, then repeat health checks.

## Accounting migration and availability

Accounting is a generation prerequisite, including for quota-free presets. Keep
`Interpretation:UsageLog:Enabled` enabled and ensure every service instance uses the
same writable SQLite database. Disabling or losing access to the database returns
503 for hosted generation; it does not disable the viewer. Quota reads must not
report a full allowance when accounting is unavailable or unresolved.

For a later authorized deployment:

1. Pause generation and drain all service instances. Stop the service for the
   migration backup; a process that may still be writing or calling the provider
   has not drained. Preserve pending and uncertain attempts for reconciliation.
2. Take a consistent SQLite backup, including committed WAL content, plus a copy
   of configuration and the previous executable. Copying only the live `.db` file
   can omit recent usage. Use SQLite's backup facility or close all connections
   cleanly before copying the database.
3. Run the new executable's usage inspection against the backup first. Migration
   is transactional and repeatable. Compare per-account historical balances and
   inspect unresolved, inconsistent, orphaned or ambiguously owned records before
   activating generation. Preserve the original backup as evidence.
4. Migrate the production ledger with generation still paused. Verify balances,
   retained client claims, pricing and timestamps; resolve any activation blockers
   through audited administrative review. Old overwritten rows may be unrecoverable:
   migration cannot reconstruct charges for which no record survives.
5. Start the service paused, verify health and accounting inspection, then resume
   generation for the controlled synthetic checks. A retry with the same authenticated
   client request ID must receive 409 without another provider dispatch.

Request-summary totals are reporting data. Authoritative charges come from attempt
receipts and labelled legacy balances, counted exactly once. Server execution IDs
identify executions; client request IDs remain separate correlation and duplicate
claim keys. Historical admitted claims survive migration and monthly resets.
No operation should erase those claims to reset quota.

A timeout, cancellation or network error after dispatch can leave cost unknown.
Unknown cost is not zero and is not released by a restart, elapsed timeout or new
month. Admission remains blocked where that accounting affects a quota-limited
account. If a valid report was generated before a bookkeeping failure, it may still
be delivered; this does not resolve the accounting hold.

## MIST administration

Trusted administrators can open the interactive console from any directory:

```bash
sudo ftitc-admintool
```

The launcher changes to `/opt/ftitc-web` and starts the web executable in
administration mode. It does not start Kestrel and does not source
`/etc/ftitc-web/interpretation.env`. Administration uses the configured paths,
whose deployment defaults are `/etc/ftitc-web/operator-codes.json` and
`/var/lib/ftitc-web/interpretation-usage.db`. Preset mappings are stored in
`/etc/ftitc-web/generation-presets.json`.

The console provides:

The main menu header shows the executable version and update time, interpretation-service
state, active-account count, total request count, and most recent request time. Each check
fails independently so a missing registry or usage database does not prevent administration.
In an interactive terminal, press a displayed number to select it immediately; Enter is
not required. Press Backspace or Esc to return from a submenu. Esc cancels any text-entry or
confirmation workflow without applying it; at the main menu Esc exits. Ctrl+C exits the tool
immediately. Text-entry prompts still use Enter to submit a value.

- **Status:** systemd state, local and public interpretation status, build and
  schema versions, operator-account totals, and usage-database statistics.
- **Operator accounts:** create accounts or show a compact `ID / Name or label / Email /
  Level` directory. Looking up an account asks directly for its exact ID and opens a
  details page with usage plus a settings menu for updating details, changing access level,
  changing quota, or revoking the account. The details lookup does not print every account.
  Opening the page immediately shows the account identity, access, quota, dates, status,
  lifetime interpretation count, and lifetime estimated cost. **All details** provides the
  period-filtered token, latency, outcome, preset, model, and recent-request breakdown.
  For a quota-limited account, the basic page also shows estimated spend subtracted from the
  effective monthly allowance, the resulting dollar and percentage balance, and the next
  UTC calendar-month reset. Billable failed attempts count. Rejections before dispatch
  incur no provider charge; missing usage after dispatch remains unknown and can block
  further quota-limited work. A displayed known subtotal is not a complete balance
  while unresolved or waived unknown costs remain.
  The page can inspect an
  account's request counts, token use, estimated cost, latency, outcomes, presets, models,
  and recent request metadata over a selected time period. A new secret
  is printed once. Listings and logs never contain the secret or its hash.
- **Logs:** list executions with their client correlation and operator-account IDs
  (or `public`), show one execution and its provider attempts, summarize
  a period with optional model/operator filters, or export metadata to CSV.
  Interactive exports default to `/home/logexports/`, with the UTC export time
  and selected horizon in the filename; an absolute custom path remains available.
- **Generation presets:** list or edit the server-supplied description and allowlisted model/reasoning mapping for quota-free,
  retrieval-disabled Summary and for Fast, Default, Advanced, and Comprehensive; edit the tier quota defaults; and edit request-size
  limits for Public, Registered, Advanced, and Administrator access. Size values are whole
  KiB from 1 through 2048. Confirmed changes
  apply immediately. Registered accounts receive $1/month and Advanced-tier accounts
  receive $3/month unless an account override changes that limit. The allowance is shared
  across Default, Advanced, and Comprehensive requests made with that capability code. Fast
  requests do not reduce the monetary allowance.

The preset IDs on the wire remain `instant`, `fast`, `standard`, and `in-depth` for
client compatibility. The public options API reports quota usage only as a whole-number
percentage remaining and a UTC reset time. The authenticated account API additionally
returns the effective dollar allowance and spend, optional account contact details, and
the all-time recorded request summary for the supplied capability code.
Preset descriptions are presentation text stored with the generation configuration. They
are returned by the options API and can be changed immediately through the interactive
admin tool or `generation-presets set-description <ID> "<text>"`; changing one does not
alter generation behavior, access, quota accounting, or provenance.

An operator account is revoked rather than deleted so historical usage remains
attributable to its non-secret record ID. The registry directory is owned by
`root:ftitc-web` with set-group-ID mode `2750`; the registry is `0640`. Atomic
registry replacements preserve owner read/write and group read permissions.

The original non-interactive commands remain available for automation:

```bash
cd /opt/ftitc-web
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name"
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --tier standard
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --tier standard --name "Person" --email "person@example.org" --organization "Lab"
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --expires-days 7
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --no-expiry
sudo dotnet AnalysisITC.Web.dll operator-code set-quota ACCOUNT_ID 2.50
sudo dotnet AnalysisITC.Web.dll operator-code set-quota ACCOUNT_ID unlimited
sudo dotnet AnalysisITC.Web.dll operator-code list
sudo dotnet AnalysisITC.Web.dll operator-code revoke ID
sudo dotnet AnalysisITC.Web.dll operator-code set-tier ID advanced
sudo dotnet AnalysisITC.Web.dll generation-presets list
sudo dotnet AnalysisITC.Web.dll generation-presets set summary gpt-5.6-luna medium
sudo dotnet AnalysisITC.Web.dll generation-presets set fast gpt-5.6-luna medium
sudo dotnet AnalysisITC.Web.dll generation-presets set-request-size public 128
sudo dotnet AnalysisITC.Web.dll usage-log status
sudo dotnet AnalysisITC.Web.dll usage-log inspect
sudo dotnet AnalysisITC.Web.dll usage-log migration status
sudo dotnet AnalysisITC.Web.dll usage-log list --since 24h --limit 100
sudo dotnet AnalysisITC.Web.dll usage-log show SERVER_EXECUTION_ID
sudo dotnet AnalysisITC.Web.dll usage-log summary --since 7d
sudo dotnet AnalysisITC.Web.dll usage-log summary --since 7d --model MODEL --operator ID
sudo dotnet AnalysisITC.Web.dll usage-log export --since 2026-09-01 --output /absolute/path/usage.csv
```

These commands also use the application configuration in `/opt/ftitc-web` and
do not require the protected provider environment file for the default paths.

## Accounting inspection and reconciliation

`usage-log inspect` lists unresolved attempts and waived unknown costs, with server
execution IDs and attempt numbers. `--operator ACCOUNT_ID` filters the account.
List, show and CSV exports distinguish the server execution ID from the client
request ID. A known subtotal is shown separately from unresolved and waived counts;
the total cost remains unknown if either count is nonzero.

Pause generation before changing accounting. The maintenance gate is stored in
SQLite and fences admission and new attempts across instances; the command also
pauses the service-availability policy:

```bash
sudo dotnet AnalysisITC.Web.dll usage-log maintenance pause --reason "Review uncertain provider usage"
sudo dotnet AnalysisITC.Web.dll usage-log maintenance status
```

Wait for active work to drain. A quota hold may remain after the work has ended;
that is expected when a charge is uncertain. If a process crashed, stop **every**
service instance before explicitly attesting that abandoned work has stopped:

```bash
sudo dotnet AnalysisITC.Web.dll usage-log maintenance confirm-stopped --all-hosts-stopped --evidence "All service instances stopped; incident reference"
```

This attestation does not settle provider charges and must not be based on elapsed
time alone. It fences abandoned executions from starting another attempt and records
an audit event. Then reconcile each uncertain attempt using exactly one of these
forms, with a provider receipt or review reference:

```bash
sudo dotnet AnalysisITC.Web.dll usage-log reconcile SERVER_EXECUTION_ID --attempt 1 --cost 0.25 --evidence "Verified provider invoice reference"
sudo dotnet AnalysisITC.Web.dll usage-log reconcile SERVER_EXECUTION_ID --attempt 1 --confirmed-unsent --evidence "Verified that dispatch never occurred"
sudo dotnet AnalysisITC.Web.dll usage-log reconcile SERVER_EXECUTION_ID --attempt 1 --waive "Administrator accepts the unresolved cost" --evidence "Review reference"
```

Use verified cost zero only when there is evidence of a zero charge. An HTTP failure,
disconnect or missing receipt is insufficient. A waiver leaves actual provider cost
unknown; it authorizes admission without inventing a free request. Reconciliation
appends an audit record and preserves the original receipt. Identical repetitions
are harmless; conflicting settlements or attempts to erase known charges fail.

After reviewing the resulting balances and holds, end accounting maintenance:

```bash
sudo dotnet AnalysisITC.Web.dll usage-log maintenance resume
```

This leaves the service-availability policy paused. Resume generation separately
through the admin tool when ready. Pending unknown charges remain visible and block
affected quota-limited accounts; neither maintenance exit nor a calendar reset
erases them. Reconciliation records include the local administrative identity and
evidence reference. Keep those references free of access codes and experimental data.

For migration blockers, `usage-log migration status` lists the surviving legacy
key, issue and review state. A historical request total may disagree with its
attempts, or no attempt breakdown may survive. Review the whole execution against
the backup and provider records, then record a verified **total**, not an amount
to add to the existing charges:

```bash
sudo dotnet AnalysisITC.Web.dll usage-log migration reconcile request:OLD_CLIENT_ID --cost 0.50 --evidence "Verified historical total; invoice reference"
sudo dotnet AnalysisITC.Web.dll usage-log migration reconcile orphan-attempt:OLD_CLIENT_ID --operator ACCOUNT_ID --started 2026-09-01T12:00:00Z --cost 0.25 --evidence "Verified owner, date and provider total"
sudo dotnet AnalysisITC.Web.dll usage-log migration reconcile request:OLD_CLIENT_ID --waive "Historical total cannot be established" --evidence "Administrative review reference"
```

These commands also require paused, drained generation. Use `--operator` only for
an account verified from external records, or `--anonymous` for verified public
work. Known ownership cannot be reassigned. A missing or invalid original start
time requires `--started` when settling a known cost, so quota periods are not
invented. An explicit waiver can leave ownership or timing unknown and accepts
the resulting historical quota uncertainty.

The immutable settlement supplies the reviewed execution total and, where needed,
verified ownership/timing through reporting views. Original records, discrepancies
and per-attempt receipts remain available. A verified total cannot be smaller than
the surviving authoritative charges and is counted once, rather than added to
them. Per-attempt allocation can remain unknown even when the verified execution
total is known. A waiver retains the known subtotal and marks the actual total
unknown. Repeating the same settlement is harmless; conflicting settlements fail.
After a historical total is settled, its original individual attempts cannot be
separately changed through the reconciliation command.
