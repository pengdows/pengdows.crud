# pengdows.crud — Implementation Evidence

This document is the companion to [`PRODUCT_THESIS.md`](./PRODUCT_THESIS.md). The thesis
states the architectural *why* and is meant to stay stable; this document tracks the
volatile *current status* — exact numeric limits, package versions, publish state,
instrument names, and internal wiring details — that changes independently of the
architecture and would otherwise make the thesis go stale every time an implementation
detail shifts. Treat everything here as a snapshot verified against source as of the date
below; re-verify against current code before quoting it externally.

Last verified: 2026-08-13.

## Ecosystem package status

| Package | Purpose | Relationship to the core architecture |
|---|---|---|
| `pengdows.crud` | Core DAL: gateways, `ISqlContainer`, dialects | The architecture itself (principles 1–10) |
| `pengdows.crud.abstractions` | Public interfaces/enums | The coordinated boundary's contract surface |
| `pengdows.crud.fakeDb` | Fake ADO.NET provider | Falsifiability for principle 10 |
| `pengdows.crud.analyzers` | Roslyn rules PGC001/008/025/026 | Compile-time enforcement, principle 9 |
| `pengdows.poco.mint.cli` + Dockerized web UI | Schema-first POCO generation | Genuinely reuses `IDatabaseContext`/`ISqlDialect` for schema inspection (verified in `DatabaseInspector.cs`) — see principle 1 |
| `pengdows.hangfire` | SQL-first Hangfire job storage | A real downstream consumer: depends on `pengdows.crud` and `pengdows.crud.analyzers`, showing the architecture generalizes past CRUD to background-job storage |
| `pengdows.stormgate` | ADO.NET connection admission control (prevents "connection storm" thundering-herd opens) | Ships from the same repository/solution as `pengdows.crud` but is a standalone, separately-adopted package — not wired into `DatabaseContext`'s own connection governance (the `SingleWriter` turnstile governor is a distinct, internal mechanism) |
| `pengdows.threading` | `ConvergeWait` + adaptive throttling | A separate general-purpose concurrency library from the same author/namespace; no dependency relationship with `pengdows.crud` exists in source as of this writing |
| `pengdows.crud.opentelemetry` | OpenTelemetry metrics adapter | Genuinely built and tested (`PengdowsMetricsObserverTests.cs`): bridges `MetricsUpdated` into `System.Diagnostics.Metrics` without adding an OTel dependency to the core package, auto-discovers both DI-registered and per-tenant contexts via `ITenantContextRegistry` events, and emits per-pool (Reader/Writer) gauges — reinforcing principle 10's Emergent Capabilities metrics claim externally. Exposes **both** naming schemes side by side: the original `pengdows.db.client.*` instruments (unchanged, so no existing consumer breaks) and, additively, OTel semantic-convention names — `db.client.operation.duration` (a real Histogram, derived from the same `ActivitySource("pengdows.crud")` spans `SqlContainer` already emits for tracing, filtered to `Track()`ed contexts via a `pengdows.context_id` Activity tag so concurrent unrelated activity is never recorded) and the connection-pool counters `db.client.connection.count`/`.max`/`.pending_requests`/`.timeouts`. Still open: the semconv pool-side histograms (`create_time`/`wait_time`/`use_time`) would require new event hooks inside `PoolGovernor`'s concurrency-critical code, deliberately deferred rather than rushed; and the OTel bridge still exposes only aggregate command/connection/transaction counts, not the `DatabaseMetrics.Read`/`.Write` per-role split. **Not yet published to NuGet** as of this writing. `docs/opentelemetry-metrics-plan.md`'s "nothing here exists" header is stale and contradicts the shipped code; `docs/FUTURE_WORK.md` has the more accurate status |

`pengdows.poco.mint` maintains its own separate test suite (`core.tests`, `api.tests`,
`IntegrationTests`, with distinct CLI/web coverage baselines) verifying schema-generation
correctness — a real testing discipline, but a separate repository's, not part of
`pengdows.crud`'s own suite described in thesis principle 10.

Download counts and latest versions (including `pengdows.poco.mint.cli`'s current release)
change continuously and are intentionally not baked into this document as fixed values —
pull current numbers from nuget.org before using them in external-facing material (sales
collateral, onboarding decks).

## Per-dialect numeric capability limits (principle 6)

`MaxOutputParameters` per-dialect cap, as currently implemented: SQL Server/Oracle 1024,
MySQL/Snowflake 65535, Firebird 1499, PostgreSQL 100.

## Internal metrics wiring status

`AttributionStats` (`pengdows.crud/metrics/AttributionStats.cs`) — a deeper internal
collector recording *why* an operation waited (pool slot vs. turnstile vs. mode lock), not
just that it did — is populated (`RecordReadRequest`/`RecordWriteRequest` are called from
`DatabaseContext.ConnectionLifecycle.cs`) but its snapshot is never read anywhere in the
codebase, including by the OTel bridge. It is real internal tracking, not yet a surfaced
capability.

## Test coverage backing specific thesis claims

Principle 10 says every claim has a specific proof rather than a general assurance. The
table below is the volatile half of that promise — exact test file names, which will
rename/move/split over time — for the claims that got a source-level audit on 2026-08-13:

| Claim (PRODUCT_THESIS.md) | Proof |
|---|---|
| Normal execution paths release their connection on every path, including exception paths (principle 5, "unleakable by accident") | `pengdows.crud.Tests/ExecuteReaderWriteConnectionLeakTests.cs` — asserts the connection is disposed when `ExecuteReaderAsync` fails before a `TrackedReader` is created |
| MySQL and MariaDB use different read-only session SQL (`transaction_read_only` vs `tx_read_only`) (principle 4) | `pengdows.crud.Tests/ReadOnlySessionSettingsTests.cs` and `pengdows.crud.Tests/dialects/MariaDbDialectTests.cs` — the latter explicitly asserts `Assert.DoesNotContain("transaction_read_only", settings)` for MariaDB |
| A transaction acquires its governed connection exactly once, not once per command inside it (principle 2) | `pengdows.crud.Tests/TransactionGovernorAcquisitionTests.cs` (added 2026-08-13) — asserts `PoolGovernor.TotalAcquired` moves by 1 for 5 commands inside one transaction, vs. by 5 for 5 sequential non-transactional commands (the contrast rules out a vacuous pass) |
| `IDatabaseContext`/`ITransactionContext` do not expose a `DataSource` property, even transitively through inherited interfaces (principle 5) | `pengdows.crud.Tests/IDatabaseContextPublicSurfaceTests.cs` (added 2026-08-13, same day the property was found public and closed) — walks each interface's full `GetInterfaces()` closure since `Type.GetProperty` doesn't search base interfaces on its own |

This table itself needs re-verification if any of the referenced test files are renamed,
merged, or deleted — it's evidence of a point-in-time audit, not a standing contract that
the tests will always exist under these names.
