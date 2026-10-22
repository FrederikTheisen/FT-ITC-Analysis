# Registration reliability investigation — 2026-10-22

## Result and scope

The initial investigation covered ITC-120, ITC-205, ITC-211, and ITC-212 against commit `3457e8e4009dd391924bd7dd531e0262a0878027` without changing production behavior. The user subsequently approved the focused fixes, implemented below on 2026-10-22. Original reproductions remain intact for before/after comparison.

The probe references the actual Web project. Its fake HTTP handler returns success locally and never opens a network connection. All databases, keys, settings, accounts, credentials, and registry files are disposable synthetic fixtures under `/private/tmp/registration-reliability-20261022/`. The production Web assembly used by the original pre-fix load run and fault probe had SHA-256: `2b9c42c9ab4971902ab99ba85a8d2c59a845ab508f232788f114feeb002a34f1`. Relevant source hashes are retained in [source-sha256.txt](evidence/source-sha256.txt).

The existing full Web suite passed **509/509**, with no failures or skips ([test output](evidence/web-suite.txt)). This is a baseline, not evidence that the four risks are fixed. The investigation's evidence checker confirms failures and controls; a successful checker run is not a production acceptance pass.

## Original findings and disposition (before fixes)

### ITC-120 — Shared mail-settings outage reproduced; not an independent healthy-mail starvation defect

Eight cases cover missing, malformed, JSON-null, and permission-denied settings for activation and access-code messages. Each case queues two accounts and calls the actual sender six times. All six calls throw; the same oldest row remains due, both rows retain zero attempts, and neither gains a delivery failure reason. No HTTP request occurs. Access-code cases additionally provision the account before the settings error, with `provisioning_succeeded=1` afterward. Restoring settings allows both messages to send.

The real background-worker outage probe independently runs with missing settings for 65 seconds, spanning two worker attempts. The queue remains unchanged and the worker logs the exception. After repair, the worker resumes without restart and delivers both messages. Exact timing is retained in [worker-outage.jsonl](evidence/worker-outage.jsonl).

**Disposition:** Reproduced, open for a focused readiness/diagnostics change. Under the user's chosen **wait for repair** policy, preserving message attempts during this shared outage is correct. Cycling through other rows cannot deliver them while the shared settings are broken. Do not describe this as one bad recipient blocking otherwise deliverable mail, or require retry exhaustion as the fix. A mail-settings outage necessarily suspends delivery; this issue alone does not establish a separate wider-traffic blocker. The improvements are explicit service-level reporting and checking readiness before provisioning.

Controls: corrupt protected payloads already fail only that delivery cycle; the next sender call delivers the following row. A retry-delayed row does not prevent a later eligible row from sending. Activation eligibility ends exactly at its existing expiry boundary. Shutdown during a blocked HTTP call leaves the row pending without consuming an attempt.

### ITC-205 — Worker pacing and launch-target failure

The real `RegistrationDeliveryWorker` runs against the unchanged production assembly and real wall clock. One hundred registrations arrive at six-second intervals over 594 seconds. Each successful activation email is consumed through `BeginActivation` immediately after `MarkSent` completes, creating its access-code delivery. The fake provider responds immediately, satisfying the agreed within-one-second assumption. Both message kinds therefore compete in the real queue.

Over **735.018 seconds**, all **100 registrations** arrived, but only **13 messages** were dispatched: **12 activation emails and one access-code email**. Adjacent dispatches were **60.004–60.072 seconds** apart. Maximum completed-message queue wait was **654.218 seconds (10 minutes 54 seconds)**, exceeding the agreed ten-minute limit. At shutdown, **99 delivery rows remained pending** and only one registration had completed both emails; the remaining workflow requires another 187 messages if every activation is consumed. Shutdown from the worker's idle delay took **5.214 ms**. The request timestamps, message kinds, queue waits, and final row snapshot are in [worker.jsonl](evidence/worker.jsonl); [summary.json](evidence/summary.json) contains the checked metrics.

The observation window deliberately stops before the roughly 3-hour complete drain implied by one message per minute. **Complete drain time is not measured.** With 200 required emails, the idealized first-to-last dispatch span is 199 minutes at exactly one dispatch per minute, before processing overhead. This is a projection from observed pacing, not an accelerated-worker test. The 100-registration scenario does not establish a 24-hour expiry incident, actual provider capacity, inbox arrival time, or a deployed backlog.

**Disposition:** Reproduced; recommend fixing before accepting the agreed 100-registration/ten-minute launch target. This is a demonstrated latency gate for that workload, not a claim that any traffic increase or release necessarily fails.

### ITC-212 — Completed operator changes lost across processes

Eight deterministic interleavings use independent .NET processes and the real registry methods. Create, revoke, details, tier, and quota execute through `InterpretationAdminCommands.RunAsync`; provisioning, regeneration, and scrubbing execute their actual registry entrypoints. The table names the paused operation A and the completed operation B whose state A subsequently overwrites.

| A resumes after B finishes | Lost effect of B | Observable consequence |
|---|---|---|
| Create → revoke, manual registry | Revocation | Original credential authenticates again. |
| Details → regenerate, manual registry | Replacement credential hash | Old credential authenticates; newly issued credential fails. |
| Quota → tier, manual registry | Tier change | Previous access tier is restored. |
| Tier → details, manual registry | Contact-detail changes | Previous name and email are restored. |
| Revoke → quota, registered registry | Quota override | Revocation survives, but the completed quota change disappears. |
| Regenerate → revoke, registered registry | Revocation | The credential returned by regeneration authenticates despite the completed revoke. |
| Scrub → provision, registered registry | New account | The concurrently provisioned account disappears. |
| Provision → revoke, registered registry | Revocation | Original registered credential authenticates again. |

**Method boundary:** A named pipe pauses an already-open registry read. The harness restores the pathname to a normal file, completes B, then supplies the original valid snapshot to A's open stream. This models an old file descriptor across atomic replacement. Provisioning is paused at its second registered-file read, which supplies the appended snapshot, rather than only its earlier validation read. No production hooks, private-method calls, assembly rewriting, or locally reimplemented registry methods are used. This is controlled fault scheduling, not a measured natural collision rate or a production incident.

Eight serialized B-then-A controls preserve B's independent changes. In the revocation-then-regeneration control, regeneration correctly refuses the revoked account. A ninth interleaving kills A while its read is blocked: B's completed revocation remains unchanged and the original credential stays denied. This proves that control only; it does not establish crash atomicity across multiple registry files or interruption during rename.

**Disposition:** Reproduced; recommend cross-process serialization before permitting normal service activity and administrative mutations to overlap. Restored credentials and lost revocations make this more consequential than a cosmetic lost update. A temporary mitigation is exclusive maintenance: stop service writers and exclude other admin processes for the mutation. No deployed collision or its frequency was measured. CR-061's separate public-access registry fix remains valid.

### ITC-211 — Failed marker reads bypass checks and permit destructive replacement

The valid-file control prevents authentication for a marked fixture and cancels its queued message without sending. Malformed, JSON-null, and permission-denied files each make `Contains` return false, allow that marked fixture to authenticate, and allow the message to send. Adding a fourth synthetic marker then succeeds and leaves only the new marker. Most decisively, the permission-denied fixture starts with three intact markers and ends with one: the old file was unreadable, not already missing its data.

The intentionally marked-but-unrevoked fixture isolates reliance on the deletion marker, representing the intermediate state after a marker is written but before scrubbing finishes. It is **not** a fully scrubbed account. A separately fully scrubbed credential remains denied in every case, including corruption, unreadability, and absence. The absent-file first-run control permits ordinary access and creation as expected. See [faults.jsonl](evidence/faults.jsonl) for before/after marker bytes and delivery state.

**Disposition:** Reproduced; recommend fixing read-error handling and refusing replacement after a failed read before relying on deletion markers operationally. Loss of markers and delivery cancellation is demonstrated; resurrection of a fully scrubbed account is not. This is a storage-failure safety condition, not evidence of a traffic-dependent capacity limit. Pending a fix, suspend marker-dependent operations when the file is unreadable and restore it from a known-good copy before mutations.

## Implemented fixes — approved follow-up

- **Mail readiness:** Validate mail settings before provisioning or sending. On missing/unreadable/malformed/null settings, return a service-unavailable result, preserve queued rows and their attempts, and check again after one minute. Preserve activation expiry and ordinary HTTP failure retries. Reuse existing health/daily-status reporting with typed settings validation and a safe service-level reason; do not label each recipient failed or expose credentials in logs.
- **Pacing:** Replace the sender's ambiguous Boolean worker contract with an internal result distinguishing empty, progress, and temporarily unavailable. A payload quarantined as failed is progress, even when no email was sent. Process serially with a one-second delay after progress and one minute after empty/unavailable; retain cancellation and daily retention. No parallel sends, queue migration, or new scheduling subsystem. The HTTP endpoint and stored row formats remain unchanged.
- **Operator writes:** Use the existing Unix cross-process lock approach for all read-modify-write methods. Acquire both canonical operator-file locks in sorted order before reading, with a bounded five-second acquisition budget and unchanged files on timeout. Retain atomic per-file replacement. If a helper is shared with the public registry, preserve that registry's timeout behavior and tests. This does not promise a transaction spanning both files.
- **Deletion markers:** Open and parse the file directly; only genuine file/directory absence represents first-run emptiness. Other read failures must remain distinguishable. Deny operator authentication, defer delivery without spending attempts, and reject registry mutations before writes. Give marker additions their own cross-process lock; nested scrubbing acquires it after the operator locks, while standalone marker additions acquire only that lock, avoiding reversed lock order. Never erase unreadable bytes. Keep the current JSON representation.

The regression expectations now assert the safe outcomes: completed updates survive; revoked credentials remain denied; unreadable marker files remain byte-for-byte intact; a configuration outage preserves queued work and resumes after repair; the launch workload meets the ten-minute maximum eligible-message wait. The full isolated Web suite passes 564/564. No Core scientific changes, deployment, commits, release metadata, or user-manual additions were made by this task.

## Post-fix validation

**Final candidate:** 564 Web tests pass, including 55 additional cases. The first 41 new regression cases all failed before production edits. They now pass, alongside existing provisioning, retry, resubmission, cancellation, public-access lock-timeout, and endpoint behavior. Mail configuration accepts both Pascal-case and camel-case property names; sender and health diagnostics share the same validation. Public submission pause does not hide mail-readiness failures while delivery remains enabled.

- Eight mail-settings cases now leave queued rows and attempt counts unchanged, make no provider calls or premature provisioning writes, and resume both messages after repair.
- The real worker outage probe retains its queue for 65 seconds and then resumes automatically; both messages finish near 121 seconds rather than the original 180 seconds.
- Invalid deletion-marker files deny authentication, defer delivery, and survive rejected additions byte-for-byte. Twenty-four regression cases reject every operator mutation across malformed, null, and permission-denied marker files. Health detects unreadable markers even when interpretation is paused. Fully scrubbed credentials stay denied.
- All eight original operator interleavings now block the competing writer until the first releases its locks. Completed mutations survive, revoked credentials stay denied, and replacement credentials remain usable. Killing a blocked writer releases its locks for the waiting writer. Concurrent standalone marker additions preserve both entries; terminating their first writer leaves the second marker intact.
- A focused regression exposed inherited lock descriptors during child-process launch. The shared native open now uses `O_CLOEXEC` on new and existing lock files. The child-inheritance regression and the partial-acquisition timeout/release test pass, as do the pre-existing public-registry timeout semantics. No multi-file transaction or crash-atomicity guarantee was added.

**Measured load result:** All **200 emails** (100 activation and 100 access-code) were dispatched by **620.037 seconds** from the first registration, following 100 arrivals over 594 seconds. Maximum eligible-message wait fell from **654.218 seconds to 56.921 seconds**, within the agreed 600-second limit. At the end of the same 735-second observation window, **zero rows remained pending**, versus 99 before the fix. Worker shutdown from its idle delay took **7.407 ms**. This is a measured drain, not the original 199-minute projection; the provider remained a local immediate-success fake. [summary.json](evidence/fixed/summary.json) records the checked results.

**Validation isolation:** Concurrent scientific edits temporarily broke the shared Core build. Tests therefore used a temporary candidate containing the recorded baseline plus only this task's nine production files and three test files. Necessary existing solution/build-content files were copied without executing any scripts. Concurrent Core, scientific instructions, UI, and tracker work were left intact. [candidate.json](evidence/fixed/candidate.json) records source hashes, baseline, SDK, and binary identity. The long load probe's [load-candidate.json](evidence/fixed/load-candidate.json) separately records its binary: the delivery and locking paths match the final candidate; the final authentication helper preserves the original operator-record-before-marker read order and was rerun through the fault probes, process races, and full suite. This validation ran on macOS; it does not claim a Linux deployment or live mail-provider test.

[Post-fix evidence](evidence/fixed/) includes the fault, race, outage, and load transcripts; [Web result](evidence/fixed/web-suite.txt) records the suite result. The full final suite transcript is retained at `/private/tmp/registration-reliability-final-web-suite4.log`. Run `check_fixed_evidence.py` against `evidence/fixed` to assert corrected behavior. The original `check_evidence.py` still checks the preserved pre-fix failures.

## Reproduce and inspect

Requires .NET 10, Python 3, and a non-root Unix account so mode `000` actually denies file reads. Run from the repository root, using fresh directories for every run. Builds produce only ordinary build artifacts. Test runners may need permission for local IPC sockets.

```sh
dotnet build Planning/Investigations/RegistrationReliability-2026-10-22/Probe.csproj --ignore-failed-sources -p:NuGetAudit=false -m:1 -nr:false
dotnet Planning/Investigations/RegistrationReliability-2026-10-22/bin/Debug/net10.0/Probe.dll faults /private/tmp/registration-reliability-new/faults
python3 Planning/Investigations/RegistrationReliability-2026-10-22/races.py Planning/Investigations/RegistrationReliability-2026-10-22/bin/Debug/net10.0/Probe.dll /private/tmp/registration-reliability-new/races --fixed
dotnet Planning/Investigations/RegistrationReliability-2026-10-22/bin/Debug/net10.0/Probe.dll worker-outage /private/tmp/registration-reliability-new/outage
dotnet Planning/Investigations/RegistrationReliability-2026-10-22/bin/Debug/net10.0/Probe.dll worker /private/tmp/registration-reliability-new/worker
python3 Planning/Investigations/RegistrationReliability-2026-10-22/check_evidence.py Planning/Investigations/RegistrationReliability-2026-10-22/evidence
```

The final command checks the retained evidence. Redirect fresh probe output to the corresponding `faults.jsonl`, `races.jsonl`, `worker-outage.jsonl`, and `worker.jsonl` filenames in a separate directory to check another run. The load probe takes 735 seconds; the corrected outage probe takes about 121 seconds (180 before the fix). No real recipients, services, registry files, or deployment tools are involved.

For the corrected concurrency schedule, append `--fixed` to `races.py`. The second process is started while the first holds its lock, verified to be waiting, and allowed to finish after the first resumes or is terminated. That mode also checks concurrent standalone deletion-marker additions. On corrected code use `check_fixed_evidence.py` for newly captured output; the original checker intentionally expects the pre-fix failures.
