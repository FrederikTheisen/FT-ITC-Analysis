# Lock permissions follow-up — 2026-10-22

The review reproduced provisioning failure when the service could read the manual registry but could not create a lock in its directory. The new regression failed at the first delivery before the fix ([failure](before.log)). It removes directory write access, verifies writes are denied, and exercises the production sender with disposable storage and a fake provider.

The manual-registry lock now lives in the registered-account directory, using SHA-256 of the full manual-registry path for its filename. Both service and CLI derive the same identity. The registered-account lock, two-lock acquisition order, five-second timeout, and partial-acquisition release remain in place. No permission broadening or deployment setup was added.

The [full Web suite](web-suite.txt) passed **566/566** against the current working tree, including provisioning, both queued messages, usable credentials, and unchanged manual-directory contents under the restricted permissions. This follow-up did not require the earlier isolated Core baseline.

The rebuilt probe passed all **11 independent-process concurrency and termination controls**. [The transcript](races.jsonl) retains ordering and file snapshots. The race assertions in `check_fixed_evidence.py` were applied to this fresh transcript, including final contents and credential validity.

Validation ran on macOS with temporary storage and fake mail. The previous long throughput run was not repeated for this lock-location change. No deployment, real mail, commits, versions, or user-manual changes.

Reviewed source hashes:

```text
6064557c4d7a7a3315f4a906df1c4fb59804ea2a56c2987de35263dfd4a9837a  AnalysisITC.Web/OperatorCodeRegistry.cs
ecce0b4956871fd3a5344899033a593a5afc53187a3a01d8b226508e60245e41  AnalysisITC.Web.Tests/RegistrationReliabilityTests.cs
```
