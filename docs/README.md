# Documentation Index

This folder holds reference material for `pengdows.crud` 2.x (currently branch 2.1). `CLAUDE.md`
(repo root) remains the canonical day-to-day development guide; the documents below go deeper on
specific subsystems.

**Product/repository boundary:** only `pengdows.crud` and `pengdows.crud.abstractions` are the
core library — the actual SQL-first data-access engine. Everything else in this repository plays a
supporting role: `pengdows.crud.fakeDb` and `pengdows.crud.opentelemetry` are real, separately
published NuGet packages, but they exist to *test* and *observe* applications built on the core
library, not to extend its data-access surface. `pengdows.crud.Tests`, `pengdows.crud.IntegrationTests`,
`testbed`, and `benchmarks/` are unshipped proof/evidence harnesses. `pengdows.stormgate`/
`pengdows.stormgate.EntityFrameworkCore` (a connection-admission controller for apps not migrating
to pengdows.crud) and the separate `pengdows.hangfire` repository (downstream transactional proof)
are adoption paths and evidence, not peer products. None of them are part of what "pengdows.crud"
means as a product.

## Reference

| Doc | Covers |
|---|---|
| [`architecture.md`](./architecture.md) | Internals deep-dive: locking, connection lifecycle, lease model, concurrency contracts |
| [`core-invariants.md`](./core-invariants.md) | Condensed cheat-sheet of the invariants in `architecture.md` |
| [`overview.md`](./overview.md) | Public API surface at a glance |
| [`advanced-types.md`](./advanced-types.md) | Value objects for network/range/interval/spatial/`HSTORE`/`rowversion` types and their built-in coercion behavior |
| [`batch-operations.md`](./batch-operations.md) | Batch create/update/upsert/delete: API, runtime behavior, architecture, dialect compatibility |
| [`generated-keys.md`](./generated-keys.md) | How `CreateAsync` retrieves an auto-generated `[Id]` value per database (`GeneratedKeyPlan` strategies) |
| [`uuid7.md`](./uuid7.md) | `Uuid7Optimized`: monotonicity scope, clock modes, throughput/backpressure, configuration |
| [`transactions.md`](./transactions.md) | `TransactionContext`, isolation profiles, savepoints, concurrency contract (commit/rollback/dispose races, reader locks, cancellation) |
| [`entity-mapping.md`](./entity-mapping.md) | Complete attribute reference: `[Table]`/`[Column]`/`[Id]`/`[PrimaryKey]`/`[Version]`/audit/`[Json]`/enum/correlation-token attributes, valid combinations, defaults |
| [`data-reader-mapper.md`](./data-reader-mapper.md) | `DataReaderMapper`/`IDataReaderMapper`: hydrate any POCO from any SQL result (stored procedures, ad-hoc queries) with no `[Table]`/`[Column]` attributes required — `MapperOptions`' `Strict`/`ColumnsOnly`/`NamePolicy`/`EnumMode` |
| [`gateway-counts.md`](./gateway-counts.md) | Gateway `COUNT(*)` helpers: `CountAllAsync`/`CountWhereAsync`/`CountWhereNullAsync`/`CountWhereEqualsAsync`, SQL shape, quoting/parameterization, and limitations |
| [`sql-container-templates.md`](./sql-container-templates.md) | `ISqlContainer.Clone()`/`Clone(IDatabaseContext)`: template reuse, parameter rebinding, cross-dialect/tenant/transaction use, disposal independence |
| [`capability-discovery.md`](./capability-discovery.md) | Reading `ISqlDialect`/`IDataSourceInformation` at runtime to branch on capability instead of database name |
| [`schema-management-boundary.md`](./schema-management-boundary.md) | What schema tooling exists today (`pengdows.poco.mint` inspection/adoption) vs. the not-yet-built generalized schema executor design; DBA-authorization and owned-object requirements |
| [`primary-keys-pseudokeys.md`](./primary-keys-pseudokeys.md) | `[Id]` vs `[PrimaryKey]` |
| [`parameter-naming-convention.md`](./parameter-naming-convention.md) | Parameter prefix conventions (`i`/`s`/`w`/`k`/`v`/`j`/`b`) |
| [`read-only-enforcement.md`](./read-only-enforcement.md) | How `ReadWriteMode.ReadOnly` is enforced per dialect |
| [`session-settings.md`](./session-settings.md) | Session-settings mechanism overview |
| [`sql-server-session-settings.md`](./sql-server-session-settings.md) | SQL Server's specific session-settings cost/tradeoff |
| [`supported-databases.md`](./supported-databases.md) | Per-database support matrix |
| [`metrics.md`](./metrics.md) | `DatabaseMetrics` fields and access pattern |
| [`opentelemetry-metrics.md`](./opentelemetry-metrics.md) | `pengdows.crud.opentelemetry` adapter design |
| [`tracing.md`](./tracing.md) | Zero-extra-package `ActivitySource("pengdows.crud")` tracing spans: tags, setup, how it complements provider-level instrumentation |

## Connection Management

| Doc | Covers |
|---|---|
| [`connection/connection-modes.md`](./connection/connection-modes.md) | Authoritative `DbMode` invariants, coercion rules, and practical guidance |
| [`connection/connection-pooling.md`](./connection/connection-pooling.md) | Database-specific pooling behavior, provider-pooling-vs-admission-control distinction |
| [`connection/ownership-and-shutdown.md`](./connection/ownership-and-shutdown.md) | What the context/transaction/reader/gateway/sentinel/registry each own, disposal ordering, the post-disposal exception contract |
| [`connection/dynamic-provider-loading.md`](./connection/dynamic-provider-loading.md) | `DbProviderLoader`: config-driven `DbProviderFactory` resolution, the section-key-vs-`ProviderName` tenant gotcha, symlink-safe `AssemblyPath` containment, recognized-vs-unknown-engine fallback, process-lifetime limitations |
| [`connection/multitenancy.md`](./connection/multitenancy.md) | `AddMultiTenancy`: context-per-tenant model, configuration shape, request-time resolution, lifecycle events, application-name composition, and the `Invalidate`/`InvalidateAll` primitives (not a designed live-rotation feature — shutdown is the intended disposal path) |
| [`connection/multitenancy-architecture.md`](./connection/multitenancy-architecture.md) | The architectural contract behind multi-tenancy: library-enforced vs. deployment-assumed guarantees, tenant-ID case rules, and `Invalidate` concurrency semantics |

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
