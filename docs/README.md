# Documentation Index

This folder holds reference material for `pengdows.crud` 2.0. `CLAUDE.md` (repo root) remains the
canonical day-to-day development guide; the documents below go deeper on specific subsystems.

## Reference

| Doc | Covers |
|---|---|
| [`architecture.md`](./architecture.md) | Internals deep-dive: locking, connection lifecycle, lease model, concurrency contracts |
| [`core-invariants.md`](./core-invariants.md) | Condensed cheat-sheet of the invariants in `architecture.md` |
| [`overview.md`](./overview.md) | Public API surface at a glance |
| [`batch-operations.md`](./batch-operations.md) | Batch create/update/upsert/delete: API, runtime behavior, architecture, dialect compatibility |
| [`transactions.md`](./transactions.md) | `TransactionContext`, isolation profiles, savepoints |
| [`primary-keys-pseudokeys.md`](./primary-keys-pseudokeys.md) | `[Id]` vs `[PrimaryKey]` |
| [`parameter-naming-convention.md`](./parameter-naming-convention.md) | Parameter prefix conventions (`i`/`s`/`w`/`k`/`v`/`j`/`b`) |
| [`read-only-enforcement.md`](./read-only-enforcement.md) | How `ReadWriteMode.ReadOnly` is enforced per dialect |
| [`session-settings.md`](./session-settings.md) | Session-settings mechanism overview |
| [`sql-server-session-settings.md`](./sql-server-session-settings.md) | SQL Server's specific session-settings cost/tradeoff |
| [`supported-databases.md`](./supported-databases.md) | Per-database support matrix |
| [`metrics.md`](./metrics.md) | `DatabaseMetrics` fields and access pattern |
| [`opentelemetry-metrics.md`](./opentelemetry-metrics.md) | `pengdows.crud.opentelemetry` adapter design |

## Connection Management

| Doc | Covers |
|---|---|
| [`connection/connection-modes.md`](./connection/connection-modes.md) | Authoritative `DbMode` invariants, coercion rules, and practical guidance |
| [`connection/connection-pooling.md`](./connection/connection-pooling.md) | Database-specific pooling behavior |

## Positioning

| Doc | Covers |
|---|---|
| [`positioning/product-thesis.md`](./positioning/product-thesis.md) | Canonical architectural/competitive reference — the 10 foundational principles |
| [`positioning/dal-taxonomy-and-comparison.md`](./positioning/dal-taxonomy-and-comparison.md) | Cross-ecosystem DAL taxonomy and comparison |
| [`positioning/implementation-evidence.md`](./positioning/implementation-evidence.md) | Volatile current-status companion to the thesis (exact limits, publish state) |

## Planning

| Doc | Covers |
|---|---|
| [`planning/future-work.md`](./planning/future-work.md) | Living backlog of open work |
| [`planning/test-setup-issues.md`](./planning/test-setup-issues.md) | Known test-infrastructure pain points |
| [`planning/db-coverage-ledger.md`](./planning/db-coverage-ledger.md) | Test coverage status for provider findings, by database |

## Other

| Path | Covers |
|---|---|
| [`perf/`](./perf/) | Benchmark evaluations and perf-sensitive implementation notes |
| [`examples/`](./examples/) | Copy-paste reference implementations (e.g. `IAuditValueResolver` for OIDC claims) |
| [`archive/`](./archive/) | Historical, superseded documents kept for record — not current guidance |
