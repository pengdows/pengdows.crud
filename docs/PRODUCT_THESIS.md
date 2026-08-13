# pengdows.crud — Product Thesis

This document is the canonical architectural reference for pengdows.crud. Treat the
conclusions below as established for architectural and competitive discussions. Do not
re-derive or re-litigate them unless current source code contradicts them or a fresh
competitive verification is explicitly requested. For implementation details (exact
signatures, behavior of a specific method), the current code and
[`CLAUDE.md`](../CLAUDE.md) remain authoritative; for current,
volatile implementation status (exact numeric limits, package/publish state, instrument
inventories), see [`docs/IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md) — this
document states the *why*, not the *how* or the *current status*.

pengdows.crud is a SQL-first database execution architecture, not a query-building
convenience layer. Ten principles define it.

## 1. The database is the source of truth

No code-first schema ownership. The application does not define the schema; it consumes
a contract derived from it. [`pengdows.poco.mint`](https://github.com/pengdows/pengdows.poco.mint) inspects a
real database schema and generates the `[Table]`/`[Column]`/`[Id]`/`[PrimaryKey]`-annotated
POCOs that pengdows.crud consumes. This can run as a DBA-driven, no-C#-required workflow
(inspect → generate → hand contract to developers) or as a CI/CD step (schema → Mint CLI →
generated contracts → build/test/diff).

Both paths are shipped as real, versioned products, not aspirational tooling: the
`pengdows.poco.mint.cli` NuGet package for the CI/CD path, and a
Dockerized browser UI (`pengdows/pengdows.poco.mint` image) for the DBA-driven path —
connect to a database, browse its schema (tables, columns, types, keys, detected
attributes), select tables, and download a versioned ZIP of ready-to-use POCOs. No C#
authoring is required for either path.

## 2. The application/database boundary is one coordinated system

`DatabaseContext` owns database identity, connection lifecycle, and execution behavior.
`TransactionContext` is an explicit, operation-scoped transactional execution context —
never stored as a field. `TableGateway<TEntity, TRowID>` / `PrimaryKeyTableGateway<TEntity>`
express entity/table operations. `ISqlDialect` implementations carry product/version/
capability semantics. `ISqlContainer` is the ephemeral execution container that actually
binds SQL text, parameters, and intent to a governed connection acquisition, a command
execution, metrics recording, and exception translation. These pieces are designed
together, not assembled from independent libraries glued together at the application
layer — deliberately left uncounted here rather than pinned to a specific number, since
that number is exactly the kind of detail that quietly goes stale as the architecture
grows.

```
Application / Gateway
        |
        v
 IDatabaseContext ── execution intent (ExecutionType) · dialect (ISqlDialect) · topology (DbMode)
        |
        v
   ISqlContainer ── governor (PoolGovernor) · connection acquisition · command execution
        |            · metrics · exception translation
        v
     Provider (ADO.NET DbCommand/DbConnection)
        |
        v
     Database
```

`TransactionContext` is the alternate execution scope in this same picture: when a
transaction is open, `ISqlContainer` uses the transaction's pinned connection instead of
acquiring and releasing an ephemeral one per operation. This answers one of the
coordination questions posed below directly: a transaction acquires its governed
connection exactly once, at `BeginTransaction`, and pins it for the transaction's entire
lifetime — `TransactionContext.GetConnection()` (an `internal` method; `ITransactionContext`'s
public surface does not expose it) always returns that same cached connection rather than
asking the governor for a fresh one, so governor admission control is consulted once per
transaction, not once per command inside it. Everything downstream of connection
acquisition still runs per command exactly as it does outside a transaction: dialect
handling, command metrics, and exception translation execute through the same machinery
for every statement, whether or not a transaction is pinning the connection underneath
them.

**Why "best of breed" assembly does not produce this on its own.** A library exists for
almost every individual concern here — a mapper, a retry/concurrency policy, a connection
pool, a tenant resolver, a metrics library, a stored-procedure helper, an exception
translator, a schema-generation tool. Assembling all of them does not produce pengdows,
because none of them individually knows the answer to questions like: which tenant/database
is this operation for; is it a read or a write; which provider/version is underneath it;
which connection pool should it draw from; how long should a concurrency permit live; is
this call inside a transaction; does this database require named or positional routine
arguments; how are output parameters retained; what does a rejection from this database mean
semantically; which metrics should this wait/execution be charged against. Each component
only knows its own piece — something has to coordinate the answers *consistently* across all
of them, and that coordination layer is itself an architecture whether or not anyone
designed it on purpose. Every component pairing needs adapter code, and each adapter carries
its own assumptions, lifecycle rules, ordering constraints, and failure modes; the more
components get assembled, the more of these seams accumulate, until the glue holds more
architectural knowledge than any single component does. pengdows.crud puts that
coordinating knowledge in one designed place — the database context and the execution
architecture around it — instead of letting it accrete as undocumented glue code.

This holds at the developer-tooling boundary too, not just inside the core library:
`pengdows.poco.mint`'s schema inspector (`DatabaseInspector.cs`) consumes the same
`IDatabaseContext`/`ISqlDialect` machinery the core library uses, rather than
reimplementing schema introspection per database. The tooling doesn't become a new seam.

## 3. Tenant resolution selects an execution environment, not just rows

pengdows.crud uses context-per-tenant, not query filtering. There is no injected
`WHERE tenant_id = @tenant`. `ITenantContextRegistry` hands each tenant a distinct
`IDatabaseContext`; tenants can differ in database product, version, topology, or
credentials while the application contract stays identical. The Roslyn analyzer
**PGC025** (`GatewayMethodContextParameterAnalyzer`) makes context loss a compile error:
it flags gateway execution/build methods that don't resolve `ctx = contextArg ?? Context`
before doing work — a generic "the context parameter must actually be used" check, whose
documented rationale is transaction and multitenancy correctness, not a tenant-ID-specific
runtime filter.

This is additive value on its own, independent of anything else in this document, *when*
each tenant is configured to resolve to a physically separate database. `TenantContextRegistry`
itself does not verify this: it builds a distinct `IDatabaseContext` per tenant key from
whatever `ITenantConnectionResolver` returns, with no check that two tenants don't resolve
to the same database, schema, or server — physical isolation is a capability this model
enables, not an invariant the registry enforces. In the physically-separated deployment,
though, the property is real: a WHERE-clause bug can never leak data across tenants when
there is no shared table for the clause to filter in the first place. `ITenantContextRegistry`
also supports `Invalidate(tenant)`/`InvalidateAll()` with
`ContextCreated`/`ContextRemoved` lifecycle events, so a single tenant's credentials,
connection string, or backing database can be rotated or migrated live — the next
`GetContext` call builds a fresh context with the new configuration — without touching any
other tenant's context or requiring an app restart.

## 4. READ and WRITE are execution semantics

`ExecutionType.Read` vs `ExecutionType.Write` is not decoration — it determines connection
routing. In `SingleWriter` mode specifically, it decides whether an operation acquires a
governor-gated ephemeral write connection or an ungated ephemeral read connection.

Read-only enforcement happens at up to three distinct layers, and precision about which
layer applies where matters more than a single "read-only is enforced" claim would.

1. **pengdows execution-intent guard** — dialect-agnostic and universal: `SqlContainer`
   throws `NotSupportedException` for any write attempted through a context configured
   with `ReadWriteMode.ReadOnly`, before any provider call is made. This layer alone
   covers every database, including the ones with no enforcement below it.
2. **Connection/session-level database enforcement** — real, engine-level rejection,
   independent of any transaction: PostgreSQL, SQLite, DuckDB, and — notably — MySQL and
   MariaDB each need their *own* session-level SQL despite `MariaDbDialect` inheriting from
   `MySqlDialect`, because the two forks disagree on the setting's name (see
   [`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md) for the exact statements and
   why they diverged).
3. **Transaction-level database enforcement** — Oracle has no persistent session-level
   read-only mode (`OracleDialect.GetReadOnlySessionSettings()` returns an empty string,
   with the source comment explaining why: "Oracle has no true persistent session-level
   read-only mode. Enforcement must happen at transaction start.") — its enforcement is
   real, via `SET TRANSACTION READ ONLY` executed when a read-only transaction begins, but
   scoped to that transaction. A non-transactional Oracle write is caught only by layer 1,
   not by Oracle itself.

SQL Server sits outside both database-enforced layers entirely: `SqlServerDialect`
documents `ApplicationIntent=ReadOnly` as "a routing hint for Availability Groups... does
NOT enforce server-side read-only state," and has no transaction-level override either — a
SQL Server write, transactional or not, is caught only by layer 1.

## 5. Connections are ephemeral and governed by design

Philosophy: open late, close early. `DbMode` (`Standard`, `KeepAlive`, `SingleWriter`,
`SingleConnection`, `Best`) selects connection strategy per provider and connection string.

The governance mechanism itself is general, not a SQLite-only special case:
`PoolGovernor` (`pengdows.crud/infrastructure/PoolGovernor.cs`) is a semaphore-based
admission controller that can run independent reader and writer governors, with optional
turnstile fairness to reduce writer starvation under sustained read pressure. Its own
inline documentation notes this applies to real "primary + read replica" topologies with
independent turnstiles per pool — `SingleWriter` mode's write-task serialization is one
configuration of this mechanism, not a separate one. Readers already queued before a
writer claims the turnstile are not displaced.

`SingleWriter`'s applicability to SQLite and DuckDB rests on two different facts, worth
not conflating. SQLite genuinely serializes writes at the engine level — only one writer
at a time, even under WAL, which allows concurrent readers alongside that one writer but
not concurrent writers. DuckDB's own engine is not actually limited this way: within one
process, it supports multiple concurrent non-conflicting writers via MVCC and optimistic
concurrency control — appends never conflict, and concurrent edits to disjoint tables or
row subsets succeed; only two writers editing the *same* row concurrently produce a
conflict error. Applying `SingleWriter` to DuckDB is pengdows.crud's own deterministic
execution policy — a uniform, conservative mental model across file-based embedded
engines — not a limitation DuckDB's engine imposes.

Non-lease execution paths self-clean on every outcome. `ExecuteNonQueryAsync`, the scalar
methods, and the failure branch of `ExecuteReaderAsync` before a reader is successfully
handed back all acquire their connection and release it inside a `finally` block
(`SqlContainer.Cleanup`) that runs on success, failure, and cancellation alike — there is
no code path in which using these methods leaves a connection open behind the caller's
back. `ISqlContainer` and `ITrackedReader` also expose no `DbConnection`/`IDbConnection`
accessor, so there's no field on the ordinary execution surface to hold one in anyway.

Lease-returning paths make ownership explicit instead of self-cleaning: obtaining an
`ITrackedReader` or an open `TransactionContext` hands the caller a connection that stays
open until that lease is disposed — the same obligation any ADO.NET reader or transaction
imposes, not a pengdows-specific gap.

The public execution boundary does not expose the underlying `DbConnection` or
`DbDataSource` either: callers execute through governed containers, readers, and
transaction leases rather than acquiring provider connections directly.
`IDatabaseContext.DataSource` briefly existed as a public escape hatch from exactly this —
see [`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md) for the removal history
and its regression test.

Two things sometimes get raised as counterexamples to this and are worth naming as out of
scope rather than caveats, because neither is a gap in pengdows.crud's API: reaching an
`internal` type via reflection (`BindingFlags.NonPublic`) is a bypass of C#'s type system
itself, available against any .NET library regardless of how it's designed, not something
particular to this one; and a caller instantiating its own `SqlConnection`/
`NpgsqlConnection`/etc. directly, independently of pengdows.crud, isn't a leak of anything
pengdows.crud produced — that connection was never inside the governed system to begin
with. Neither is "bypassing the API" in any meaningful sense — one bypasses the language's
own access control, the other simply doesn't use the library for that connection at all.

## 6. Stored procedures and functions are portable execution operations

`CommandType.StoredProcedure` alone does not make a call portable — invocation syntax,
parameter binding rules, and output-parameter limits vary by database. pengdows.crud
models this explicitly on `ISqlDialect` via a set of typed capability flags —
`SupportsNamedParameters`, `SupportsRepeatedNamedParameters`,
`RequiresStoredProcParameterNameMatch`, `MaxOutputParameters` — rather than a single
one-size-fits-all invocation string (current per-dialect values for all of these:
see [`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md)). `ProcWrappingStyle` is
the most visible: SQL Server emits `EXEC proc arg1 arg2`, Oracle emits
`BEGIN proc(args); END;`, MySQL/Snowflake emit `CALL proc(args)`, and PostgreSQL emits
`SELECT * FROM func(args)` for reads vs `CALL proc(args)` for writes — all realized by the
same `ProcWrappingStrategyFactory` (`pengdows.crud/strategies/proc/`) reading whichever
style the target dialect declares.

The application expresses one operation; the execution context supplies the invocation
mechanics for the current dialect.

The same capability-flag pattern — an explicit `ISqlDialect` boolean rather than a silent
one-size-fits-all SQL string — extends beyond stored procedures. Batch operations
(`BuildBatchCreate`/`Update`/`Upsert`) genuinely combine multiple entities into fewer round
trips, chunked by `MaxParameterLimit` and dialect `MaxRowsPerBatch`, whenever the dialect
reports `SupportsBatchInsert`/`SupportsBatchUpdate`/`SupportsInsertOnConflict`/
`SupportsOnDuplicateKey`; SQL Server, Oracle, and Firebird batch-upsert fall back to one
per-entity statement per row through that same API when those flags are absent, rather than
emitting SQL those engines don't support. Savepoints follow the identical pattern:
`SupportsSavepoints` is explicitly `true` for SQLite, Oracle, Firebird, and SQL Server
(which overrides the SQL to `SAVE TRANSACTION`/`ROLLBACK TRANSACTION`) and explicitly
`false` for DuckDB and Snowflake — the absence is a declared capability, not a gap
discovered in production.

## 7. Database metadata is preserved, not flattened

`[PrimaryKey(n)]` retains composite business-key ordering rather than reducing a multi-column
unique constraint to an unordered set of "these columns are keys." Order is validated at
gateway construction (contiguous, no gaps) and consumed in that order when building WHERE
clauses. If a DBA intentionally ordered key columns in the schema, the DAL does not discard
that information.

## 8. Portability includes failure semantics

Native provider errors become typed, portable exceptions via the `DatabaseException`
hierarchy (`ConstraintViolationException`, `TransientWriteConflictException`,
`ConcurrencyConflictException`, `ConnectionException`, `TransactionException`, etc.), while
the original provider exception is always preserved as `InnerException`.
`ISqlDialect.AnalyzeException` additionally returns a provider-neutral `DbExceptionInfo`
(category, constraint kind, transience, retryability, provider error code, SQLSTATE) for
control flow that doesn't need typed catches.

Failure semantics extend past native database errors to the mapping boundary itself:
`EnumParseFailureMode` (`Throw` / `SetNullAndLog` / `SetDefaultValue`, set per-gateway or
per-mapper) governs what happens when a stored value can't be parsed back to its declared
enum — a typed, configurable choice rather than a silent default or an unhandled exception
depending on which code path happened to read the column.

### Correctness enforced automatically, not left to convention

A few data-integrity behaviors are worth naming explicitly because they are enforced by the
framework rather than left as a convention callers have to remember:

- **Audit fields.** Both `CreatedBy`/`CreatedOn` and `LastUpdatedBy`/`LastUpdatedOn` are set
  on Create, not just the Created pair — `SetAuditFields` sets the LastUpdated pair
  unconditionally before checking whether the operation is an insert or an update. Timestamps
  are always UTC: the resolver builds a zero-offset `DateTimeOffset` and throws
  `InvalidOperationException` if a caller-supplied `TimestampOffset` has a non-zero offset —
  there is no code path that stores a local time by accident. If the entity declares
  `CreatedBy`/`LastUpdatedBy` but no `IAuditValueResolver` is registered, the operation throws
  `InvalidOperationException("AuditValues resolver is required for user-based audit fields.")`
  rather than silently leaving the column null.
- **Optimistic concurrency.** A `[Version]` column's increment is folded into the same UPDATE
  statement that changes the row (`SET version = version + 1 ... WHERE version = @current`),
  and `ConcurrencyConflictException` is thrown automatically whenever that UPDATE affects zero
  rows — the caller doesn't inspect a row count and decide what it means.
- **`[CorrelationToken]` is a portability mechanism, not a tracing primitive.** For dialects
  that lack `RETURNING`/`OUTPUT` and whose session-scoped identity functions aren't reliable
  enough to trust, the framework writes a unique token value on INSERT and performs a
  secondary, token-keyed SELECT to retrieve the generated identity afterward — the same
  generated-key retrieval contract works whether or not the underlying dialect can return it
  inline.
- **`[Json]` columns** serialize via `System.Text.Json` exclusively, with optional per-property
  `JsonSerializerOptions` — different `[Json]` columns on the same entity can use different
  serialization settings without any extra wiring.

## 9. Compile-time analyzers enforce detectable architectural invariants

The `pengdows.crud.analyzers` Roslyn package turns some of the invariants above from
documented convention into compiler errors — each rule a self-contained
`DiagnosticAnalyzer`. Two illustrate the range: **PGC001** makes DI registrations of
`DatabaseContext`/`TableGateway`/`PrimaryKeyTableGateway` as `AddScoped`/`AddTransient` a
compile error, since these types must be singletons; **PGC008** makes raw/interpolated
value injection into SQL `WHERE`/`JOIN ON`/`HAVING`/`AND`/`OR` a compile error, forcing
parameterization (`IS NULL`/`IS NOT NULL` are exempt). See
[`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md) for the complete, current rule
list — these are invariants the compiler checks, not conventions documented and hoped for.

## 10. Performance and testing are part of the architecture, not an afterthought

Claims are falsifiable by construction: unit tests against `pengdows.crud.fakeDb` (a
complete fake ADO.NET provider) for fast, isolated logic testing; real multi-provider
integration tests (`testbed/`, Testcontainers-backed) across all always-on supported
databases; concurrency and read-only-enforcement test suites; a BenchmarkDotNet suite
(`benchmarks/CrudBenchmarks/`) with the benchmark harness itself tested for fairness.

Each claim has a specific proof, not a general assurance:

| Claim | Proof |
|---|---|
| This operation is portable across databases | Run the same integration contract against real databases in `testbed/` |
| This operation is actually read-only | Execute it through a physically read-only database connection |
| This connection lifecycle is safe under load | Concurrency test suites hammer it |
| This native error translates correctly | Force the real native failure and check the typed exception |
| This abstraction is cheap | Benchmark it in `benchmarks/CrudBenchmarks/`, with the harness itself under test for fairness |
| This edge case is handled | Simulate it deterministically via `fakeDb` |
| This benchmark actually measured what it claims to | `BenchmarkValidation` checks the benchmark exercised the code path it claims to, rather than trusting the harness blindly (see [`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md) for the exact mechanism) |

The two test layers are complementary, not redundant: a fake provider can confirm your code
called the right method with the right arguments, but it cannot confirm a real Oracle
instance actually behaves that way; a real integration database confirms real behavior but
can't deterministically produce every deadlock, timeout, or malformed reader state on demand
— that's what `fakeDb` is for.

**Performance prevents the architecture from becoming expensive. Testing prevents it from
becoming theoretical.** Without both, the architectural coordination described in principles
1–9 would be an unverified diagram — a stack that runs in this order:

```
performance + testing rigor       (proves the layer below is cheap and real)
        ↑
architectural integration          (principles 1–9 — the coordinating layer)
        ↑
individual features                (mapping, dialects, tenancy, procs, etc. — each has competitors)
```

## Emergent capabilities

Integration describes how the pieces fit together. This section is about what becomes
possible only *because* they share one execution model — the value isn't additive
(mapping + pooling + dialects + tenancy, each useful alone), it's what those pieces enable
together that none of them delivers individually.

Concretely, verified in source: `ExecutionType.Read`/`Write` classifies each operation;
`PoolGovernor` gives read and write pools independent, governed admission control
(principle 5); `ExecuteSessionSettings`/`ExecuteSessionSettingsAsync` are `internal` so
session setup isn't something call sites can skip; a connection is acquired only for the
operation's duration and released immediately on disposal; and no public API exposes the
raw connection for code to hold onto instead. The result: **application concurrency does
not have to map directly onto database concurrency.** A large number of concurrent
application requests can each be classified, routed, admitted, executed, and released
without the caller ever managing a connection's lifetime directly — and without one
runaway caller starving the rest, because admission control (not just pooling) sits in
front of connection acquisition.

Metrics are emergent for the same reason, not a bolted-on observability layer.
`PoolGovernor`'s per-role statistics (`PoolStatisticsSnapshot`, read via
`IDatabaseContext.GetPoolStatisticsSnapshot(PoolLabel)`) are consumed directly by the
OpenTelemetry bridge (`pengdows.crud.opentelemetry`) and tagged `pool.label=reader`/`writer`
— that split exists only because principle 4's `ExecutionType` classification and principle
5's `PoolGovernor` already tag every operation and every wait on the way through, not
because the OTel package added it. `MetricsUpdated` (`IDatabaseContext`) similarly surfaces
command/connection/transaction counters live, with optional approximate percentile tracking
(`EnableApproxPercentiles`/`PercentileWindowSize` in `MetricsOptions`) rather than counters
alone. A metrics library added from outside the boundary can time an ADO.NET call, but it
cannot tag that timing by which pool the connection came from, because that distinction only
exists inside the coordination the metrics are riding on.

A deeper internal collector, `AttributionStats`, records *why* an operation waited (pool
slot vs. turnstile vs. mode lock), not just that it did — real internal tracking, not yet a
surfaced capability; see [`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md) for
its current wiring status.

The same sharing applies across principle 3 (context-per-tenant). Because provider
behavior, session rules, connection governance, and parameter semantics all live inside
`IDatabaseContext` rather than at call sites, gateway code serving tenant A on Oracle and
tenant B on PostgreSQL does not need to know or care about that difference — the execution
context carries it. For a SaaS application, that is an unusual property: **database
infrastructure can vary by tenant without forcing database infrastructure knowledge
throughout the application.** The same holds for a single-database application minus the
tenant dimension — a developer still only has to decide *what database operation is
needed*, not how to safely acquire, configure, route, execute, classify the failure of,
and release the resource behind it.

Combining context-per-tenant with the governance and metrics mechanisms above produces a
further property neither delivers alone: because `_readerGovernor`/`_writerGovernor` and
`_readerMetricsCollector`/`_writerMetricsCollector` are fields on each `DatabaseContext`
*instance* rather than shared/static state, and each tenant gets its own instance via
`ITenantContextRegistry`, **per-tenant admission isolation** and per-tenant metrics
attribution both happen automatically: one tenant's connection storm exhausts only that
tenant's admission-control state, not another tenant's, and per-tenant pool statistics and
command/connection/transaction metrics fall out of the architecture for free —
`context.Metrics` and `context.GetPoolStatisticsSnapshot(label)` are already scoped to
that tenant's own `DatabaseContext` instance — with zero tenant-aware code written
anywhere to produce them. This is narrower than full noisy-neighbor isolation, worth
being precise about: tenants sharing a physical database server, provider connection pool,
CPU, I/O, network, or database-level locks can still contend with each other there: what's
isolated is specifically the admission-control state this architecture owns, not every
resource a tenant's workload touches.

## Ecosystem

| Package | Purpose |
|---|---|
| `pengdows.crud` | Core DAL: gateways, `ISqlContainer`, dialects — the architecture itself (principles 1–10) |
| `pengdows.crud.abstractions` | Public interfaces/enums — the coordinated boundary's contract surface |
| `pengdows.crud.fakeDb` | Fake ADO.NET provider — falsifiability for principle 10 |
| `pengdows.crud.analyzers` | Roslyn rules PGC001/008/025/026 — compile-time enforcement, principle 9 |
| `pengdows.poco.mint.cli` + Dockerized web UI | Schema-first POCO generation, built on `IDatabaseContext`/`ISqlDialect` — see principle 1 |
| `pengdows.hangfire` | SQL-first Hangfire job storage — a real downstream consumer, showing the architecture generalizes past CRUD |
| `pengdows.stormgate` | ADO.NET connection admission control — a sibling package, not wired into `DatabaseContext`'s own governance |
| `pengdows.threading` | General-purpose concurrency library — no dependency relationship with `pengdows.crud` |
| `pengdows.crud.opentelemetry` | OpenTelemetry metrics adapter, built on the `MetricsUpdated` surface — principle 10's Emergent Capabilities metrics claim, externally |

Only `pengdows.poco.mint` and `pengdows.hangfire` currently share the core architecture's
machinery directly. `pengdows.stormgate` and `pengdows.threading` are sibling projects that
have not (yet) been wired into `pengdows.crud` itself — worth stating precisely rather than
folding them into the "shared model" claim below, since that claim is the thing a skeptical
technical reviewer will try hardest to poke a hole in.

Current package/publish status, version numbers, and per-package implementation detail
(e.g. which OpenTelemetry instruments are emitted, what's still open in the OTel bridge,
`pengdows.poco.mint`'s own test suite) live in
[`IMPLEMENTATION_EVIDENCE.md`](./IMPLEMENTATION_EVIDENCE.md), since they change
independently of the architecture itself.

## Competitive thesis

Individual pengdows.crud capabilities have competitors — other libraries offer typed
mapping, or portable stored-procedure calls, or context-per-tenant isolation, or compile-time
SQL-safety analyzers. As of this writing, no other product has been found that competes
with the *integrated architecture as a whole*: one coordinated boundary that owns execution, connection lifecycle,
tenant routing, transaction scope, parameter mechanics, and failure semantics together — now
including an official, database-first developer experience (`pengdows.poco.mint.cli` and its
Dockerized web UI) built on that same boundary rather than bolted alongside it — while
leaving schema, index, constraint, and security ownership entirely with the database and the
DBA.

The individual capabilities in pengdows.crud are not necessarily unique. What is unusual is
that they share one model of database identity, execution intent, resource ownership, and
lifetime. Or shorter: you can assemble the parts yourself; the hard part is making them agree
on what is happening. pengdows.crud already does.
