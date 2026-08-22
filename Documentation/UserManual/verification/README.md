# Unpublished verification records

This directory supports editorial traceability and must not be published with the manual.

## Verification baseline

- Product: FT-ITC Analysis 1.4.3
- Commit: `7a19b583468b4b087e130e4b27c8140cd428339a`
- Commit date: 2026-08-21
- Audit date: 2026-08-22
- Audit host: macOS 15.7.4, arm64
- Cross-platform runtime: .NET SDK 10.0.302
- Interfaces: Avalonia build and native macOS application

The shared core, interface source, application builds, and tests are primary evidence. The project wiki, README, and bundled help are secondary evidence. A screenshot is evidence only for the state it depicts; it is not proof of a complete workflow.

## Status vocabulary

- `verified` - the public claim is supported by current product evidence and the relevant check passed.
- `conditional` - behavior is supported but depends on data, metadata, platform services, or a compatible result; the condition is documented.
- `outside_scope` - a discovered capability is deliberately excluded from the user manual.

The `method` field distinguishes live UI observation, automated tests, source inspection, and secondary editorial evidence. Re-audits must not replace a stronger method with a weaker one without recording the reason.

## Freshness procedure

When the maintainer requests an audit:

1. Diff product changes since the baseline commit.
2. Select every matrix row affected by changed readers, commands, labels, state, calculations, or exports.
3. Repeat the listed task and cross-platform check.
4. Update evidence, status, and `last_verified` in the matrix.
5. Update a public page's `last_verified` only after all affected rows on that page pass.

No fixed cadence or release gate is implied.

