# Repository Guidelines

## Mandatory Workflow And Reviews
- TDD is mandatory for every behavior change, bug fix, regression fix, and public-contract change.
- Start by writing or updating an automated test that fails for the intended reason before changing implementation.
- Do not start implementation until the test is red; after implementation, rerun the relevant automated tests and do not consider the work complete until they pass with no skipped tests introduced.
- If automated coverage is genuinely not possible, say so explicitly and document the verification gap.
- All reviews are done against [REVIEW_POLICY.md](./REVIEW_POLICY.md).
- Review output, merge guidance, blocker/major/minor classification, required evidence, and minimal patch guidance must follow [REVIEW_POLICY.md](./REVIEW_POLICY.md).
- If instructions overlap, follow the more stringent requirement. If this file conflicts with [REVIEW_POLICY.md](./REVIEW_POLICY.md) on review behavior, follow [REVIEW_POLICY.md](./REVIEW_POLICY.md).

## Core Philosophy

`pengdows.crud` is an opinionated, high-performance, SQL-first data access framework built on a **database-first** philosophy. It provides **"Prego features"** — expert-level, built-in solutions to difficult real-world data access problems that developers often assume are handled by their tools but usually are not. It is designed to be more robust and feature-rich than a micro-ORM like Dapper, while retaining high performance and developer control, without the pitfalls of heavier ORMs like EF Core.

No LINQ, no tracking, no surprises — explicit SQL control with database-agnostic features.

## Architectural Classification: Why pengdows.crud is NOT in Standard DAL Categories

Traditional classifications place data access tools on a 1D spectrum from **Heavy ORMs** (EF Core, Hibernate) to **Micro-ORMs** (Dapper, sqlx). `pengdows.crud` breaks this spectrum by occupying the **Explicit SQL + High Execution Governance** quadrant:

1. **NOT an ORM / Unit of Work**: No change tracking, no entity state machines, no LINQ-to-SQL translation, no dirty checking.
2. **NOT a Micro-ORM / Mapper like Dapper**: While it offers zero-overhead mapping, it provides full **execution lifecycle governance** (`PoolGovernor`, adaptive `DbMode` coercion, turnstiles, ANSI session normalization, dialect capability synthesis, and audit rollback) that Dapper completely ignores.
3. **NOT a Query Builder like jOOQ / SqlKata**: `ISqlContainer` allows SQL building, but its primary duty is binding SQL, parameters, and intent to a governed connection lifecycle and transaction lease.
4. **Core Thesis**: `DatabaseContext` is a **singleton execution coordinator** that acts as the single execution authority for pools, admission, dialects, transactions, and metrics.
5. **Canonical Comparison Reference**: See [`docs/positioning/dal-taxonomy-and-comparison.md`](./docs/positioning/dal-taxonomy-and-comparison.md) for full comparisons across .NET, Java, Go, Rust, and Python.

## Project Structure & Module Organization


- Source: `pengdows.crud/` (core), `pengdows.crud.abstractions/` (interfaces), `pengdows.crud.fakeDb/` (in-memory provider), `testbed/` (integration suite via Testcontainers).
- Tests: `pengdows.crud.Tests/` (xUnit), `pengdows.crud.IntegrationTests/`. Coverage and TRX under `TestResults/`.
- Solution: `pengdows.crud.sln`. CI: `.github/workflows/deploy.yml`.
- Benchmarks: `benchmarks/CrudBenchmarks/` (BenchmarkDotNet suite; run before shipping perf-sensitive changes).
- Tools: `tools/` — `interface-api-check`, `verify-novendor`, `run-tests-in-container.sh`.

## Build, Test, and Development Commands

```bash
# Restore
dotnet restore

# Build (treats warnings as errors for libraries)
dotnet build pengdows.crud.sln -c Release

# Test (local)
dotnet test -c Release --results-directory TestResults --logger trx

# Run specific test
dotnet test --filter "MethodName=TestMethodName"
dotnet test --filter "ClassName=MyTests"

# Test with coverage (CI-like)
dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"

# Integration suite (requires Docker)
dotnet run -c Release --project testbed

# Verify API baseline (run after any interface changes)
dotnet run --project tools/interface-api-check/InterfaceApiCheck.csproj -c Release -- \
  --generate \
  --baseline pengdows.crud.abstractions/ApiBaseline/interfaces.txt \
  --assembly pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll

# Verify no vendor directories committed
dotnet run --project tools/verify-novendor

# Pack (NuGet)
dotnet pack <project>.csproj -c Release
```

- `testbed` is the integration testing app; treat it as part of the primary verification flow.
- Whenever work is completed, ensure all unit tests and integration tests pass with no skipped tests.
- If the intended functionality is unclear, consult the wiki (`pengdows.crud.wiki/`) or ask for clarification before proceeding.

## Coding Style & Naming Conventions

- C# 12 on `net8.0`; `Nullable` and `ImplicitUsings` enabled.
- File-scoped namespaces; keep lowercase namespaces (`pengdows.crud.*`).
- Indentation: 4 spaces; follow existing brace style; prefer expression-bodied members when clearer.
- Minimize public APIs; make types/members `internal` when possible. `WarningsAsErrors=true`.
- Organize by domain folders: `attributes/`, `dialects/`, `connection/`, `threading/`, `exceptions/`.
- Refer to the project as `fakeDb` (lowercase f, uppercase D) in paths/docs.

## API Visibility Principles

- Program to interfaces whenever possible; concrete types should primarily exist to satisfy the interface contracts, and consumers should depend on the abstractions located under `pengdows.crud.abstractions`.
- Expose `ITableGateway`, `IDatabaseContext`, `ISqlContainer`, etc., as the official surface area for `pengdows.crud` and keep implementation types internal unless there is a compelling reason to document them directly.
- Hide implementation details as `internal` by default. Prefer factory/DI creation. Public constructors are allowed for core entry points (`DatabaseContext`, `TableGateway<,>`, tenant helpers) and should remain deliberate and documented.

## Interface-first Mandate

- Always code against the interface contract; implementation classes exist only to fulfill the abstractions defined in `pengdows.crud.abstractions`.
- Treat interfaces such as `ITableGateway`, `IDatabaseContext`, `ISqlContainer`, `ISqlDialect`, etc. as the primary SDK surface — new code should depend on those APIs rather than concrete helpers.
- Keep concrete types internal unless a public contract is required; avoid adding new public constructors unless there is a clear SDK-use reason.

## Interfaces & Extension Points

- `IDatabaseContext`: entry point. Create via DI or `new DatabaseContext(connStr, DbProviderFactory)`. Builds `ISqlContainer`, formats names/params, and controls connections/transactions.
  - `Dialect` property (`ISqlDialect`) — the SQL dialect in use for this context
  - `ModeLockTimeout` property (`TimeSpan?`) — timeout for mode/transaction locks; `null` = wait indefinitely
  - `ReaderPlanCacheSize` property (`int?`) — plan cache size for reader connections
- `ISqlContainer`: compose SQL safely and execute.
  Example: `var sc = ctx.CreateSqlContainer("SELECT 1"); var v = await sc.ExecuteScalarRequiredAsync<int>();`
- `ITableGateway<TEntity, TRowID>`: SQL-first CRUD with inspectable containers.
  Example: `var sc = helper.BuildRetrieve(new[] { id }); var e = await helper.LoadSingleAsync(sc);`
- `ITransactionContext`: `using var tx = ctx.BeginTransaction(); ... tx.Commit();` Pass `tx` to helper methods when you want execution inside the transaction.
- `IAuditValueResolver`: implement to supply user/time; register in DI so audit fields populate consistently.
- Advanced: implement `ISqlDialect`/`IDbProviderLoader` to add/override provider behavior.

## Three-Tier API (TableGateway)

**Tier 1 — Build methods** (SQL generation only, no DB I/O):
```csharp
ISqlContainer BuildCreate(entity);
ISqlContainer BuildBaseRetrieve("alias");   // SELECT with no WHERE — starting point for custom queries
ISqlContainer BuildRetrieve(ids, "alias");  // SELECT ... WHERE id IN (...)
ISqlContainer BuildDelete(id);
ISqlContainer BuildUpsert(entity);
ISqlContainer sc = await BuildUpdateAsync(entity);  // Only async Build method
```

**Tier 2 — Load methods** (execute a pre-built container):
```csharp
TEntity? result                  = await LoadSingleAsync(container);
List<TEntity> list               = await LoadListAsync(container);
IAsyncEnumerable<TEntity> stream = LoadStreamAsync(container);  // Memory-efficient streaming
```

**Tier 3 — Convenience methods** (Build + Execute in one call):
```csharp
bool created = await CreateAsync(entity);
int affected = await UpdateAsync(entity);
int affected = await DeleteAsync(id);
int affected = await UpsertAsync(entity);
TEntity? e   = await RetrieveOneAsync(id);           // By [Id]
TEntity? e   = await RetrieveOneAsync(entityLookup); // By [PrimaryKey]
List<TEntity> list = await RetrieveAsync(ids);
IAsyncEnumerable<TEntity> stream = RetrieveStreamAsync(ids);
```

**Three-Tier API (PrimaryKeyTableGateway)**

`PrimaryKeyTableGateway<TEntity>` is for entities with **no surrogate `[Id]` column** — all ops keyed on `[PrimaryKey]` columns. Throws `SqlGenerationException` at construction if entity has no `[PrimaryKey]`.

```csharp
// Tier 1 — Build
ISqlContainer BuildCreate(entity);
ISqlContainer BuildBaseRetrieve("alias");
ISqlContainer BuildRetrieve(entityList, "alias");   // WHERE by [PrimaryKey]
ISqlContainer BuildUpsert(entity);
ISqlContainer sc = await BuildUpdateAsync(entity);
IReadOnlyList<ISqlContainer> BuildBatchCreate/Update/Upsert/Delete(entities);

// Tier 2 — Load (same as TableGateway)
TEntity? result = await LoadSingleAsync(container);
List<TEntity> list = await LoadListAsync(container);
IAsyncEnumerable<TEntity> stream = LoadStreamAsync(container);

// Tier 3 — Convenience
bool created  = await CreateAsync(entity);
TEntity? e    = await RetrieveOneAsync(entityLookup); // By [PrimaryKey] only — no id overload
int affected  = await UpdateAsync(entity);
int affected  = await DeleteAsync(entityCollection);  // No DeleteAsync(id)
int affected  = await UpsertAsync(entity);
// Batch: BatchCreateAsync / BatchUpdateAsync / BatchUpsertAsync / BatchDeleteAsync
```

**ISqlContainer execution methods return `ValueTask` (not `Task`):**
```csharp
ValueTask<int>            ExecuteNonQueryAsync(commandType);
ValueTask<T>              ExecuteScalarRequiredAsync<T>(commandType);
ValueTask<T?>             ExecuteScalarOrNullAsync<T>(commandType);
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(commandType);
ValueTask<ITrackedReader> ExecuteReaderAsync(commandType);
```

**Clone for reuse:**
```csharp
var clone = container.Clone();              // Same context, update param values
var clone = container.Clone(txContext);     // Different context (transaction, multi-tenancy)
```

## CRITICAL: Pseudo Key (Row ID) vs Primary Key (Business Key)

**DO NOT CONFUSE THESE CONCEPTS.**

| Concept | Attribute | Columns | Purpose |
|---------|-----------|---------|---------|
| **Pseudo Key / Row ID** | `[Id]` | Always single | Surrogate identifier for TableGateway operations, FKs, easy lookup |
| **Primary Key / Business Key** | `[PrimaryKey(n)]` | Can be composite | Natural key — why the row exists in business terms |

**Key Rules:**
1. `[Id]` and `[PrimaryKey]` are MUTUALLY EXCLUSIVE on a column — never both on the same property
2. `TableGateway<T,TId>` requires `[Id]` for row-id operations (`UpdateAsync`, `DeleteAsync(TRowID)`, `RetrieveOneAsync(TRowID)`). `CreateAsync` supports `[PrimaryKey]`-only entities.
3. `[Id(false)]` = DB-generated (autoincrement); `[Id]` or `[Id(true)]` = client-provided
4. `[PrimaryKey]` defines business uniqueness, enforced via UNIQUE constraint in DDL
5. Both can coexist on different columns: pseudo key for operations, business key for domain integrity
6. `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns; `DeleteAsync(TRowID)` uses `[Id]`
7. **Choosing the gateway:** entity has `[Id]` → `TableGateway<TEntity, TRowID>`; entity has only `[PrimaryKey]` → `PrimaryKeyTableGateway<TEntity>`

```csharp
[Table("order_items")]
public class OrderItem
{
    [Id(false)]           // Pseudo key — DB auto-generates
    [Column("id")] public long Id { get; set; }

    [PrimaryKey(1)]       // Business key part 1
    [Column("order_id")] public int OrderId { get; set; }

    [PrimaryKey(2)]       // Business key part 2
    [Column("product_id")] public int ProductId { get; set; }
}
```

## Id Attribute: Writable vs Non-Writable

| Attribute | Meaning | INSERT behavior |
|-----------|---------|-----------------|
| `[Id]` or `[Id(true)]` | Client provides value | Id column included in INSERT |
| `[Id(false)]` | DB generates value (autoincrement/identity) | Id column omitted from INSERT |

**SQL Server note:** Attempting to insert a value into an IDENTITY column throws an error unless `SET IDENTITY_INSERT ON`.

## Version Column (Optimistic Concurrency)

The `[Version]` attribute enables optimistic concurrency control:

| Operation | Behavior |
|-----------|----------|
| **Create** | If version is null/0, automatically set to 1 |
| **Update** | Increments version by 1 in SET clause; adds `WHERE version = @currentVersion` |

**Conflict detection:** `UpdateAsync` automatically throws `ConcurrencyConflictException` when a `[Version]` column is present and the UPDATE affects 0 rows (version mismatch or row deleted by another process).

## Upsert Behavior

`UpsertAsync` / `BuildUpsert` determines insert vs update based on conflict key:

1. **Primary choice:** `[PrimaryKey]` columns (if any defined)
2. **Fallback:** `[Id]` column ONLY if writable (`[Id(true)]` or `[Id]`)
3. **Error:** Throws if no `[PrimaryKey]` AND `[Id]` is not writable (`[Id(false)]`)

**SQL generated depends on database:**
- SQL Server/Oracle: `MERGE`
- PostgreSQL: `INSERT ... ON CONFLICT`
- MySQL/MariaDB: `INSERT ... ON DUPLICATE KEY UPDATE`

## Multi-Tenancy

pengdows.crud uses **context-per-tenant** (not query filtering):

- Each tenant gets a separate `DatabaseContext` (different connection string/database)
- Request resolves which context to use — no additional filtering required
- **No "WHERE tenant_id = X" injection** — tenants are physically separated
- Each tenant can use a different database type (SQL Server, PostgreSQL, MySQL, etc.)

```csharp
var tenantCtx = registry.GetContext(tenantId);
var order = await gateway.RetrieveOneAsync(orderId, tenantCtx);
await gateway.CreateAsync(newOrder, tenantCtx);
```

## ExecutionType (Read vs Write)

`ExecutionType` declares intent so the context can provide the appropriate connection:

| Type | Intent | Connection behavior |
|------|--------|---------------------|
| `ExecutionType.Read` | Read-only operation | May get ephemeral or shared connection |
| `ExecutionType.Write` | Modifying operation | Gets write-capable connection |

In `SingleWriter` mode, this determines whether you acquire a governor-gated ephemeral write connection or an ungated ephemeral read connection.

## TypeMapRegistry.Register<T>()

**Explicit registration is NOT required.** `GetTableInfo<T>()` uses `GetOrAdd` — auto-builds on first access.

```csharp
// These are equivalent:
typeMap.Register<MyEntity>();           // Explicit pre-registration
typeMap.GetTableInfo<MyEntity>();       // Auto-registers on first call
new TableGateway<MyEntity, long>(ctx);  // Also triggers auto-registration
```

## JSON Mapping Defaults

JSON CLR types (`JsonDocument`, `JsonElement`, `JsonNode`, `JsonValue`) are auto-detected as JSON columns by the type map. `[Json]` remains available for explicit override/clarity.

## Enum Storage

Enum storage format is determined by `DbType` in the `[Column]` attribute:

| DbType | Storage |
|--------|---------|
| `DbType.String` | Stored as enum name (string) |
| Numeric (`Int32`, etc.) | Stored as underlying numeric value |

**Throws** if DbType is neither string nor numeric.

## RetrieveOneAsync(TEntity) Requirements

`RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns to find the row.

**If no `[PrimaryKey]` defined:** Throws `"No primary keys found for type {TypeName}"`

Use `RetrieveOneAsync(TRowID id)` for lookup by pseudo key instead.

## CRITICAL: Audit Field Behavior

**BOTH CreatedBy/On AND LastUpdatedBy/On are set on CREATE.**

This is intentional design — it allows "last modified" queries without checking if the entity was ever updated.

| Operation | CreatedBy | CreatedOn | LastUpdatedBy | LastUpdatedOn |
|-----------|-----------|-----------|---------------|---------------|
| **Create** | SET | SET | SET | SET |
| **Update** | unchanged | unchanged | SET | SET |

**Requirements:**
- If entity has `[CreatedBy]` or `[LastUpdatedBy]`, you MUST provide `IAuditValueResolver`
- Without resolver + user audit fields = `InvalidOperationException` at runtime
- Time-only audit fields (`[CreatedOn]`, `[LastUpdatedOn]`) work without resolver (uses `DateTime.UtcNow`)
- The audit resolver ALWAYS returns UTC timestamps; DateTime, DateTimeOffset, and TimestampOffset are all supported.

## Connection Management and DbMode

**Philosophy:** Open connections late, close them early. Respect database-specific quirks.

| Mode | Value | Use Case |
|------|-------|----------|
| `Standard` | 0 | **Production-supported** — pool per operation, client-server databases |
| `PreventDatabaseUnload` (`KeepAlive` is an obsolete alias) | 1 | **Production-supported for single-machine/embedded deployments** — SQL Server LocalDB, where the sentinel connection keeps the engine from unloading between requests (not SQLite/DuckDB — those coerce to SingleWriter instead) |
| `SingleWriter` | 2 | **Production-supported** — file-based SQLite/DuckDB, serializes writes via turnstile governor |
| `SingleConnection` | 4 | Every read and write serializes through one pinned connection. **Never production-suitable for `:memory:` SQLite/DuckDB** — see below. Narrower, durable-storage uses (e.g. Firebird embedded to a `.fdb` file) are structurally viable but currently lack any connection-repair path; treat as scoped/niche rather than general production. |
| `Best` | 15 | Auto-select optimal mode based on provider and connection string |

- **SingleWriter**: The turnstile governor serializes write *tasks* (not connections) preventing database locking errors. Note: readers already queued before a writer grabs the turnstile are not displaced.
- **Best**: Automatically selects the safest and most performant `DbMode` based on the provider and connection string.

**`SingleConnection` against an in-memory database (`:memory:`) is not production-suitable, and this is structural, not a current limitation to be fixed.** The entire database lives inside that one connection's process memory: no independent persistence, no crash recovery, no backup, and it cannot survive a process restart or even a dropped connection (a fresh connection to the same `:memory:` string creates a new, empty database). Reserve `:memory:` for tests and ephemeral scratch data.

**`PreventDatabaseUnload` has zero to do with performance or optimization — it exists to work around one specific piece of external engine behavior.** SQL Server LocalDB automatically unloads the entire database (shuts its own engine instance down) once it observes no active connections for a while. Under plain `Standard` mode (open late, close early), a quiet period with no traffic lets the pool drain to zero open connections, LocalDB unloads, and the next request pays the cost of LocalDB relaunching and reattaching the database file before it can even open a connection. `PreventDatabaseUnload`'s entire job is to prevent exactly that: it holds one pinned, idle connection open for the life of the `DatabaseContext`, purely so LocalDB always sees at least one active connection and never decides to unload. That sentinel connection is **never used to run a single command** — every real read and write still goes through its own fresh, ephemeral connection exactly like `Standard` mode. Removing the sentinel wouldn't slow down a single query; it would just let LocalDB unload during idle periods again. That's the whole feature. The old name `KeepAlive` is retained only as an `[Obsolete]` compatibility alias.

## Transactions

Transactions are **operation-scoped** — create inside methods, never store as fields.

```csharp
using var txn = ctx.BeginTransaction();
// or with portable isolation profile:
using var txn = ctx.BeginTransaction(IsolationProfile.SafeNonBlockingReads);

await txn.SavepointAsync("checkpoint1");
await txn.RollbackToSavepointAsync("checkpoint1");
txn.Commit();
```

**CRITICAL: Do NOT use `TransactionScope`**

`TransactionScope` is incompatible with pengdows.crud's connection management. The "open late, close early" philosophy means each operation opens/closes its own connection, which causes distributed transaction promotion to MSDTC and broken transactional semantics. Always use `ctx.BeginTransaction()`.

## DI Lifetime Rules

| Component | Lifetime | Why |
|-----------|----------|-----|
| `DatabaseContext` | **Singleton** | Manages connection pool, metrics, DbMode state |
| `TableGateway<T,TId>` | **Singleton** | Stateless, caches compiled accessors |
| `IAuditValueResolver` | **Singleton** | Must be thread-safe/AsyncLocal-based (e.g. `IHttpContextAccessor`) |
| `ITenantContextRegistry` | **Singleton** | Manages per-tenant contexts |

## Extending TableGateway — The Correct Pattern

**Inherit from TableGateway to add custom query methods.** Don't wrap it in a separate service class.

```csharp
public interface IOrderGateway : ITableGateway<Order, long>
{
    Task<List<Order>> GetCustomerOrdersAsync(long customerId);
}

public class OrderGateway : TableGateway<Order, long>, IOrderGateway
{
    public OrderGateway(IDatabaseContext context, IAuditValueResolver resolver) : base(context, resolver) { }

    public async Task<List<Order>> GetCustomerOrdersAsync(long customerId)
    {
        var sc = BuildBaseRetrieve("o");
        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("o.customer_id"));
        sc.Query.Append(" = ");
        var p = sc.AddParameterWithValue("cid", DbType.Int64, customerId);
        sc.Query.Append(sc.MakeParameterName(p));
        return await LoadListAsync(sc);
    }
}
```

## CRITICAL: Test-Driven Development (TDD) — MANDATORY

**ALL CODE MUST BE WRITTEN USING TDD. THIS IS NON-NEGOTIABLE.**

### TDD Workflow (Follow This Exactly)
1. **WRITE THE TEST FIRST** — Before ANY implementation code
2. **RUN THE TEST** — Verify it fails (red)
3. **WRITE MINIMAL IMPLEMENTATION** — Just enough to make the test pass (green)
4. **REFACTOR** — Improve code while keeping tests green
5. **REPEAT** — For every feature, bug fix, or change

### TDD Rules
- **NEVER** write implementation code before tests
- **NEVER** skip writing tests for "simple" changes
- **NEVER** commit code without corresponding tests
- Tests define the expected behavior — write them to lock in desired outcomes
- If you're unsure what to implement, the test will tell you

### Testing Infrastructure
- Framework: xUnit; mocks: Moq. Name files `*Tests.cs` and mirror source namespaces.
- Prefer `pengdows.crud.fakeDb` for unit tests; avoid real DBs. Use `testbed/` for integration via Testcontainers.
- Coverage artifacts live in `TestResults/`; CI publishes Cobertura from `TestResults/**/coverage.cobertura.xml`.
- The entire unit-test suite currently finishes in under 30 seconds; if a run approaches three minutes, terminate it and investigate for locking/hanging issues immediately.
- CI enforces minimum **83% coverage**; target **95%** for new work.
- Expand `fakeDb` when tests need behaviors it lacks — don't bypass its limitations.

## Adding a New Database

**Every new database added to `SupportedDatabase` requires a complete integration test suite.** No exceptions.

**Verify capability flags explicitly for the new database.** `ISqlDialect`'s `Supports*` capability flags (`SupportsJoins`, `SupportsMerge`, `SupportsWindowFunctions`, `SupportsJsonTypes`, `SupportsTemporalData`, etc.) are concrete boolean properties on each dialect. In the base `SqlDialect` class, universal baseline capabilities (joins, subqueries, group by, transactions) default to `true`, while advanced features (`SupportsMerge`, `SupportsWindowFunctions`, `SupportsCommonTableExpressions`, `SupportsArrayTypes`, `SupportsJsonTypes`, etc.) default to `false`. Concrete dialects override each capability explicitly with version-aware logic where appropriate (e.g., `SupportsWindowFunctions => !IsInitialized || IsVersionAtLeast(...);`). Verify each capability flag against the engine's *actual* behavior rather than assuming conformance.

### Checklist

1. **Enum value** — add to `pengdows.crud.abstractions/enums/SupportedDatabase.cs`
2. **Dialect** — create `pengdows.crud/dialects/<Name>Dialect.cs`, register in `SqlDialectFactory.cs`
3. **Test container** — create `testbed/<Name>/<Name>TestContainer.cs` (start, get context, dispose)
4. **Test provider** — create `testbed/<Name>/<Name>TestProvider.cs` and override `CreateTable()`. `testbed` was consolidated with `pengdows.crud.IntegrationTests` (both used to run largely the same battery independently): `TestProvider`'s base `RunTest()` now only does container-provisioning-adjacent concerns — table creation, the scalar-UDF check, and the `DbMode`/`PreventDatabaseUnload` idle-unload probe cluster — plus a `RunAdditionalTestsAsync()` virtual hook for anything that's still genuinely testbed-only (a check that needs the live container itself, not just a connection string). Everything else — CRUD round-trips, parameter binding, transactions/isolation, stored procedures, upsert/error-mapping/identifier-quoting capability probes, pool isolation, kill-connection rollback behavior, DbMode lock-contention scenarios — belongs in `pengdows.crud.IntegrationTests` (see `Core/`, `ErrorHandling/`, `DatabaseSpecific/`) as ordinary xUnit `[SkippableFact]`s using `DatabaseTestBase`/`IntegrationTestFixture`, not as a `TestProvider` override. Only add a `TestProvider` override for something that cannot be expressed as an xUnit test against a pooled connection string (e.g. `CreateTable()`'s own DDL quirks, or a genuinely container-level probe).
5. **Always-on registration** — add to the `configurations` list in `ParallelTestOrchestrator.GetTestConfigurations()` (not in an opt-in block)
6. **fakeDb fixture** — add `pengdows.crud.fakeDb/xml/<Name>.schema.xml` (the `GetSchema()`/`GetSchema(string)` payload `fakeDbConnection` serves for this `EmulatedProduct`; copy the closest wire-compatible family member's file verbatim — e.g. a Postgres-wire-compatible database should start from `AuroraPostgreSql.schema.xml`, which is itself an exact copy of `PostgreSql.schema.xml`). Skipping this doesn't fail at compile time or even at dialect-unit-test time — it only surfaces the first time a fakeDb-based test actually calls `GetSchema()` for the new database, as a `FileNotFoundException: Embedded schema not found: pengdows.crud.fakeDb.xml.<Name>.schema.xml`. This is exactly what happened with Spanner: `SpannerDialect` and `SqlDialectFactory` registration both landed correctly, but the schema fixture never did, and `DatabaseContextTests.CanInitializeContext_ForEachSupportedProvider(Spanner)` failed on that missing resource until the fixture was added.
7. **Unit tests** — add a dedicated `<Name>DialectTests.cs` in `pengdows.crud.Tests/dialects/` asserting every capability flag/override the new dialect actually sets (not just the ones covered incidentally by other tests) — see `SpannerDialectTests.cs` for the pattern. Skipping this is easy to miss precisely *because* nothing fails: the dialect still compiles, `SqlDialectFactory` still registers it, and every capability-gated integration test (`[SkippableFact]` checks like `SupportsSavepoints`/`ProcWrappingStyle`) still passes by skipping cleanly — so a wrong override value (e.g. a capability flag that should be `false` left at its inherited `true`) has no unit test locking it down and no integration test catching it either. This is exactly what happened with Spanner: `SpannerDialect.cs` existed with a full set of overrides (`SupportsMerge`, `SupportsSavepoints`, `SupportsOverridingSystemValue`, `SupportsSetValuedParameters`, `ProcWrappingStyle`, isolation levels) for an entire prior session with zero dialect-level test coverage, and nothing in CI or the test suite ever surfaced that gap on its own.

### Easy-to-miss spots (found the hard way with Db2 — check these every time)

These are places outside the dialect file itself that switch or pattern-match on `SupportedDatabase` explicitly. A new database silently falls through to a `default`/catch-all branch here instead of erroring, so nothing fails loudly — only manual review catches it.

8. **The new dialect's `GetSupportedIsolationLevels(bool)` and `GetIsolationProfileMapping(bool)` overrides** (in `pengdows.crud/dialects/<Name>Dialect.cs`, not `IsolationResolver.cs` — `IsolationResolver` only orchestrates profile resolution/degradation/validation now, it asks the dialect for the actual per-database data). Missing an override silently gives the database `SqlDialect`'s generic ANSI fallback (ReadCommitted/RepeatableRead/Serializable for every level and profile), which is wrong for any database with non-standard isolation semantics — check the isolation levels the engine genuinely enforces (not just what its SQL parser accepts; TiDB parses `SERIALIZABLE` but silently treats it as `REPEATABLE READ`) against real behavior, not assumed from a similar dialect.
9. **`ISqlDialect.IsClientServerDatabase` / `DetectInMemoryKind` / `CoerceConnectionMode`** — a new client-server RDBMS dialect needs **no action here at all**: the base `SqlDialect` defaults are `IsClientServerDatabase => true`, `DetectInMemoryKind => InMemoryKind.None`, and `CoerceConnectionMode` honors any explicit request and resolves `DbMode.Best` to `Standard`. `DatabaseContext.Initialization.cs` owns no per-database mode-coercion rules of its own anymore — `CoerceMode()`/`DetectInMemoryKind()` just call the dialect and log whatever `(Mode, Reason)`/`InMemoryKind` comes back. Override only if the new engine has a *real* mode restriction: embedded/single-writer engines (SQLite, DuckDB — override all three: `IsClientServerDatabase => false`, a real `DetectInMemoryKind`, and `CoerceConnectionMode` via the shared `SqlDialect.CoerceEmbeddedSingleWriterMode` helper) or a topology-specific forced mode like SQL Server LocalDB (override only `CoerceConnectionMode`, see `SqlServerDialect`). This used to be three separate hardcoded `SupportedDatabase` switches in `DatabaseContext.Initialization.cs` that each had to independently list every client-server/full-server database, and anything missed from any of them either silently suppressed the SingleConnection/SingleWriter mode-mismatch warning, produced a misleading "unknown provider" log message, or (worse) just used the wrong coercion rule outright. Get the dialect's overrides right (or take the defaults) and every call site is automatically correct — see `pengdows.crud.Tests/dialects/DialectCoerceConnectionModeTests.cs` and `DialectDetectInMemoryKindTests.cs` for the per-dialect contract to satisfy.
10. **`pengdows.crud/dialects/SqlDialect.cs` → `GetNaturalKeyLookupQuery()` and any other pagination/"first row only" fallback** — check whether the new database's syntax actually matches the generic `LIMIT 1` fallback. It doesn't for Oracle (`ROWNUM = 1`), and it doesn't for Db2 (`FETCH FIRST 1 ROWS ONLY`). Grep `SqlDialect.cs` for `DatabaseType ==`/`DatabaseType !=` checks and verify each one explicitly for the new database rather than assuming the catch-all branch is correct.
11. **Exception classification — constraint-kind is now single-sourced from the dialect; a second, narrower system still exists for category-level metrics.** `IDbExceptionTranslator.Translate(ISqlDialect dialect, ...)` (produces the typed `DatabaseException` subclass a caller catches) now takes the dialect itself and delegates its Unique/ForeignKey/NotNull/Check classification directly to `dialect.IsUniqueViolation`/`IsForeignKeyViolation`/`IsNotNullViolation`/`IsCheckConstraintViolation` — see item 22 for the full story, including why this was worth doing (a live, provable classification disagreement) and one deliberate, documented exception (Sybase). `SqlDialect.TryClassifyProviderException` (protected, produces `DbErrorCategory` for Deadlock/SerializationFailure/Timeout/ReadOnlyViolation — categories the four public `IsXxx` booleans don't cover) remains a separate, narrower mechanism feeding `ClassifyException`/`AnalyzeException`'s advisory API and a metrics-only fallback path — see item 23 for a third, previously-undocumented classifier in this same area. A new database with its own translator AND its own dialect overrides still needs both, but constraint-kind logic itself no longer needs to be independently re-derived in the translator — write it once on the dialect.
12. **`SqlDialect.GetBaseSessionSettings()` — check for session-level state that leaks across a pooled connection.** Research whether the new database has connection/session registers that (a) are not reset by transaction commit/rollback, and (b) can silently change the meaning of a subsequent caller's SQL if a prior borrower altered them (e.g. Db2's `CURRENT ISOLATION`, `CURRENT TEMPORAL SYSTEM_TIME`/`BUSINESS_TIME`). Only add settings here that are pooled-connection-hygiene invariants pengdows.crud must enforce for correctness — never application policy (default schema, lock-timeout tuning, query-optimizer knobs, application name) which belongs to the caller's own configuration, not a dialect baseline. Verify live against a real container before committing to the SQL syntax and to whether the driver accepts multiple semicolon-separated `SET` statements in one `ExecuteNonQuery` call — some drivers (Oracle) reject multi-statement batches outright and need a `BEGIN...EXECUTE IMMEDIATE...END` wrapper instead.
13. **`ISqlDialect.ProcWrappingStyle` — decide the real stored-procedure call syntax; don't leave the `SqlDialect` base default (`ProcWrappingStyle.None`) unexamined.** `None` silently means "stored procedures unsupported" with no compile error and no loud test failure — `pengdows.crud.IntegrationTests/Core/StoredProcedureTests.cs`'s per-provider checks just skip cleanly for a provider they don't recognize, so a database that actually supports procedures can sail through review looking "done" while the capability is simply never implemented. This is exactly what happened with Db2: `CallProcWrappingStrategy`'s own doc comment already lists "MySQL, MariaDB, DB2" as the SQL-standard `CALL proc(args)` style it was written for, but `Db2Dialect` never actually set `ProcWrappingStyle => ProcWrappingStyle.Call` — the gap was later logged as "deliberately deferred" in project notes with no actual investigation behind that label. Pick the correct `ProcWrappingStyle` value (`Call`, `Exec`, `PostgreSQL`, `Oracle`, `ExecuteProcedure`, or a genuinely new one) by checking the database's real stored-procedure invocation syntax against `pengdows.crud/strategies/proc/*.cs`, add a case to `StoredProcedureTests.cs`'s per-provider DDL/call switch, and add dialect-level and integration coverage for it — don't accept `None` without writing down *why* in the dialect file.
14. **Actually connect the new `<Name>TestContainer` to a live database before calling the integration suite done — "compiles and is registered in `ParallelTestOrchestrator`" is not evidence it works.** A container/adapter args mismatch can look complete (dialect done, container class written, provider written, registered) while the connection itself never succeeds. This is exactly what happened with Spanner: `SpannerOmniTestContainer.cs` (and its `pengdows.crud.IntegrationTests/SpannerOmniIntegrationTests.cs` twin) started PGAdapter with `-p emulator-project`, but Spanner Omni's own CLI creates the database under project `default` — with `autoConfigEmulator=true` this made PGAdapter try to auto-create a `default` instance under the wrong project, which the single-instance emulator rejects (`UNIMPLEMENTED: CreateInstance not allowed`), and every connection attempt then timed out at 120s. It shipped and looked done on paper; only an actual live connect-and-query caught it. When adding a multi-container setup (emulator + protocol adapter, or similar), run it once by hand — `docker run`/`docker exec` the same images and flags the `TestContainer` uses, then a client against the resulting port — before trusting the C# harness's own timeout-and-retry loop to tell you it's wired correctly.
15. **`pengdows/internal/DatabaseDetectionService.cs`'s flavor-refinement probes (Aurora/SingleStore/Spanner/Yugabyte/etc.) live in ONE shared core, `DetectFlavorCoreAsync`, used by both the sync entry point (`DetectFromConnectionWithDetail`, used by `DatabaseContext`'s ordinary, synchronous constructor — the path essentially every real caller goes through) and the async one (`DetectFromConnectionWithDetailAsync`, used by `DataSourceInformation.CreateAsync`).** This used to be two independent hand-written implementations that each needed the new database's discriminator probe added separately — a probe added to only one of them would compile clean, pass any fakeDb-based unit test that happened to exercise only that path, and *still fail silently at runtime*, exactly what happened with Spanner (the async twin got a `SHOW SPANNER.OPTIMIZER_VERSION` probe, the sync twin didn't, and every real Spanner connection silently ran as plain `PostgreSqlDialect`). It was unified by isolating the one genuine sync/async difference — blocking `ExecuteScalar()` vs. real `ExecuteScalarAsync` — behind an `executeScalar` delegate parameter, so `DetectFlavorCoreAsync`'s branching/gating logic (which probe runs for which base product, in what order) exists exactly once; the sync entry point drives it with a delegate that wraps the blocking call in an already-completed `Task` and unwraps it via `GetAwaiter().GetResult()` (safe because nothing inside ever performs real async I/O on that path, so the await always completes synchronously — no thread-blocking risk). **Add a new database's discriminator probe once, inside `DetectFlavorCoreAsync`** — it takes effect on both paths by construction. Add a unit test to `pengdows.crud.Tests/internal/DatabaseDetectionSyncAsyncParityTests.cs` covering the new probe (asserting `DetectProduct` and `DetectProductAsync` agree) rather than duplicating a test per path.
16. **A wire-protocol-compatible-but-not-fully-compatible database (rides an existing dialect via inheritance, e.g. a Postgres-wire proxy/emulator) can still break every operation once ADO.NET connection pooling kicks in, even after the dialect and detection are both correct — check what your driver sends when a pooled connection is returned, not just what your own SQL generation sends.** Npgsql issues `SET SESSION AUTHORIZATION DEFAULT;RESET ALL;` on pool-return by default (disable via `No Reset On Close=true` in the connection string), and a real vanilla PostgreSQL accepts it fine — but a proxy that only implements a subset of PostgreSQL's SQL surface may reject it outright. This is exactly what happened with Spanner: once the detection fix above was applied and PGAdapter connections actually pooled correctly, the *first* CRUD operation on any recycled connection failed with PGAdapter's own `P0001: Invalid SET statement: SET SESSION AUTHORIZATION DEFAULT. Expected TO or =.` — and because `DatabaseTestBase.RunTestAgainstAllProvidersAsync` throws (failing the whole xUnit test) if *any* one provider in its loop fails, this one new database's driver-pooling incompatibility looked like it had broken the *entire* integration suite (100+ ostensibly unrelated test methods failing) rather than just its own tests. When a failure like this suddenly spans many unrelated test classes right after adding a database, suspect the shared all-providers test harness before assuming you broke that many independent things — check whether just the new provider is failing inside the aggregate exception. Pooling itself should stay **on** — pengdows.crud assumes provider-level pooling is always enabled, it is not an optional mode to disable as a workaround.
17. **A database that rides an existing dialect via inheritance (e.g. a Postgres-wire proxy) can still reject the *type* your driver infers for a CLR type by default, even when your own SQL/DDL text is completely correct — check what type OID your driver actually puts on the wire, not just the column type you declared.** Spanner's PostgreSQL interface has no plain `timestamp` (without time zone) type *at all* — verified live: Npgsql's default inferred type for a `DateTime` parameter (`NpgsqlDbType.Timestamp`) is rejected outright with `P0001: Type <timestamp> is not supported.`, regardless of the target column's own declared type (even a `TIMESTAMPTZ` column — real PostgreSQL tolerates the parameter/column type mismatch via an implicit assignment cast; PGAdapter does not). The fix is a `RegisterMapping<DateTime>`/`RegisterMapping<DateTimeOffset>` entry in `AdvancedTypeRegistry.cs`'s `RegisterTemporalMappings()` that explicitly forces `NpgsqlDbType.TimestampTz` via `SetEnumProperty` (see the Spanner entries there for the pattern) — the generic `DbType.DateTime` path is not enough on its own. Note this is `AdvancedTypeRegistry`'s `RegisterMapping` system specifically — it runs *before*, and takes precedence over, `pengdows.crud/types/coercion/ProviderParameterFactory.cs`/`ParameterBindingRules.cs`'s coercion-registry path (`SqlDialect.CreateDbParameter`'s `s_primitiveClrTypes` fast-path check bypasses both for actual primitives, but `DateTime`/`DateTimeOffset`/`bool`/`Guid` are deliberately excluded from that fast path specifically so dialects can register exactly this kind of per-provider override) — a fix added only to the coercion-registry layer will never run.
18. **`SqlDialect.DetectDatabaseInfoAsync` re-derives `DatabaseType` from scratch when the dialect's own `InferDatabaseTypeFromInfo` falls through to its assumed base type — and for a new database with no distinctive version-string marker (rides another engine's dialect via inheritance), that fallback triggers on *every* call, potentially disagreeing with the detection pass that already correctly picked this dialect subclass in the first place.** This is intentional and necessary for some databases (see the Aurora MySQL case: a plain `MySqlDialect` is deliberately re-examined here to discover it's actually Aurora), but for a database whose *own* dialect was already correctly selected by a prior live-probe detection pass (e.g. `SqlDialectFactory.CreateDialect`/`CreateDialectAsync` already found "Spanner" and built a `SpannerDialect`), letting this method independently re-ask the same question is redundant at best. Verified live: for a real Spanner connection, this redundant second pass non-deterministically re-resolved to plain PostgreSql instead of Spanner, silently corrupting `IDatabaseContext.Product` (and anything downstream that switches on it, e.g. `TestTableCreator`'s own provider routing) back to the wrong value — even though the dialect instance actually running all SQL generation (`SpannerDialect`) stayed correct throughout, making the bug invisible in anything that inspects the dialect directly and only visible in code that trusts `context.Product`. `SqlDialectFactory.CreateDialect`/`CreateDialectAsync` now set `SqlDialect.PreDeterminedDatabaseType` immediately after their own detection pass specifically so `DetectDatabaseInfoAsync` trusts that answer instead of re-deriving it — nothing to add here for a new database unless its own dialect needs the *opposite* behavior (deliberate post-construction flavor refinement, like Aurora), in which case leave `PreDeterminedDatabaseType` unset for that code path.

19. **Column-type-name mapping (e.g. "what's the DATETIME/BOOLEAN/BIGINT type name for this provider") for tests that build a small custom table now lives in one place: `IntegrationObjectNameHelper.BigIntType`/`IntType`/`StringType`/`DecimalType`/`DateTimeType`(`SupportedDatabase)`.** This used to be independently reinvented in `Core/AuditFieldTests.cs`, `Core/CompositeKeyTests.cs`, `Core/MergeConflictTests.cs`, and `Core/VersionedUpsertConflictTests.cs` — a new database's type quirk (Spanner's missing plain `TIMESTAMP`, item 17) had to be independently rediscovered and independently fixed in every one of these, and three of the four copies were only found via a `grep` for the literal broken value after the first fix landed, not by running the affected tests. Consolidated into `IntegrationObjectNameHelper` (see `IntegrationObjectNameHelperTypeTests.cs` for the full per-provider characterization these methods are locked to) — **use these methods for any new small custom-table test rather than writing another private switch.** `Infrastructure/TestTableCreator.cs`/`TypeHydrationTableCreator.cs` are a separate, coarser problem (each duplicates the *entire* per-provider `CREATE TABLE` DDL, not just a type-name lookup) and were intentionally left out of this consolidation — grep those two specifically for a new database's type quirk in addition to checking `IntegrationObjectNameHelper`.

20. **Individual integration test files can hide their OWN private per-provider DDL-builder switch, entirely separate from `IntegrationObjectNameHelper`/`TestTableCreator.cs`/`TypeHydrationTableCreator.cs` — grep for a bespoke `switch`/`if` on `SupportedDatabase` inside the specific test file you're adding coverage for, not just the shared infrastructure classes.** This is exactly what happened with Spanner: `pengdows.crud.IntegrationTests/ErrorHandling/ConstraintViolationTests.cs` builds its own `test_related` table via a private DDL switch that never had a `SupportedDatabase.Spanner` case at all, throwing `NotSupportedException: Provider Spanner not supported for related table` — and this had nothing to do with the (already-fixed, already-consolidated per item 19) `IntegrationObjectNameHelper` type-mapping gap. It went undetected for as long as it did specifically because Spanner's earlier connectivity/detection bugs (items 14-18) meant its tests never ran far enough to reach this switch — a database can look "wired up" and still have per-test-file DDL gaps waiting behind every earlier bug that gets fixed. After adding this one missing case, `ConstraintViolationTests` went from 0/14 to 5/14 passing for Spanner; the other 9 failures turned out to be unrelated (see item 21) and confirmed via an `INTEGRATION_ONLY`-scoped run to **not** be regressions from any other change — always re-run a file's full test class for the new database after fixing one gap in it, since one `NotSupportedException` can mask several independent downstream failures.
21. **Being routed to an exception translator in `DbExceptionTranslatorRegistry` (item 11) is not proof that translator actually classifies the new database's real exceptions correctly — verify live, don't trust the routing table alone.** Spanner is correctly routed to `PostgresExceptionTranslator`, but live testing originally showed `NotNullViolationException`/`CheckConstraintViolationException` coming back as generic `DatabaseOperationException`, and UNIQUE/CHECK violations not throwing at all. **Both root causes are now confirmed, via a real exception dump captured from a live failing call (written to a temporary file, since `Console.Error.WriteLine` from production code is silently NOT captured by xUnit's per-test output — write diagnostics to a file, not the console, if you ever need to do this again):**
    - **UNIQUE/CHECK not throwing at all had nothing to do with exception classification.** `ConstraintViolationTests.cs`'s own `AddUniqueConstraintAsync`/`AddCheckConstraintAsync` helpers (a *second* private per-provider DDL switch in this same file, beyond the one item 20 already found and fixed) both fell to a `_ => null` default for Spanner — meaning the constraint was never added to the table at all, so nothing could ever violate it. This is the exact item-20 pattern (a test file's own bespoke per-provider switch, independent of any shared helper) recurring a second time in the same file; grep an integration test file for *every* private `SupportedDatabase`-keyed switch it has, not just the first one you find.
    - **`NotNullViolationException`/`CheckConstraintViolationException` misclassification is a genuine, confirmed Spanner behavioral difference: Spanner returns SqlState `"P0001"` (a generic raise-exception code) for these constraint violations, not the ANSI class-23 codes (`23502`/`23514`) real PostgreSQL uses.** `PostgreSqlDialect`'s inherited `IsNotNullViolation`/`IsCheckConstraintViolation` are pure SqlState checks, so they silently return `false` for every Spanner constraint violation of these two kinds. Fixed with message-pattern overrides on `SpannerDialect` (the same approach Sqlite/Firebird already use for their own non-standard shapes) using the real captured message text: `"P0001: name must not be NULL in table test_table."` for NotNull, `` "P0001: Check constraint `test_table`.`chk_value_positive` is violated for key (1)" `` for Check. **`IsUniqueViolation` needed no override** — a genuine unique-index violation on Spanner apparently surfaces through the real `23505` SqlState correctly (only the synthesized-constraint-style violations, NotNull/Check, get funneled through the generic `P0001` path) — verified live, not assumed; re-verify per-constraint-kind on any future Spanner-adjacent database rather than assuming one dialect's SqlState behavior generalizes across all four constraint kinds.
    - **Fixing `IsNotNullViolation`/`IsCheckConstraintViolation` (item 22's constraint-kind system) was not sufficient on its own — `TryClassifyProviderException` (item 11/23's separate, category-level `DbErrorCategory` system) also needed a Spanner override.** `ClassifyException`'s base-class generic message fallback only recognizes a constraint violation via keywords like `"constraint"`/`"violates"` — Spanner's real NotNull message (`"... must not be NULL in table ..."`) contains neither, so `AnalyzeException(...).Category` kept returning `Unknown` even after the constraint-kind exception TYPE was already correct. (The Check message happened to pass by pure coincidence — it contains the literal word `"constraint"` — which would have hidden this gap if NotNull hadn't been checked too; don't trust one accidentally-passing case to mean the category-level system is fine.) Fixed by overriding `SpannerDialect.TryClassifyProviderException` to reuse the same four `IsXxxViolation` predicates `IDbExceptionTranslator.Translate` already delegates to, so the two systems classify identically by construction rather than by coincidence. **Takeaway for any future database with non-standard SqlStates: fixing the constraint-kind checks (item 22) and the category classifier (item 11/23) are two separate steps — verify both live, independently, even though they're closely related.**
    - **A foreign-key-violating insert used to hang for the full command timeout (60s) before throwing `CommandTimeoutException` instead of `ForeignKeyViolationException`, reproducibly, across every FK test variant.** This was NOT a classification bug — the real underlying exception genuinely was a client-side read timeout (`TimeoutException` inside `NpgsqlException("Exception while reading from stream")`), meaning Spanner/PGAdapter itself was not responding promptly when an insert violated a foreign key. **Re-verified live 2026-09-14 and no longer reproduces**: `ErrorHandling/ConstraintViolationTests.cs`'s three FK tests (`ForeignKeyViolation_InvalidReference_ThrowsException`, `ForeignKeyViolation_InvalidReference_ClassifiesAsConstraintButNotUnique`, `ForeignKeyViolation_DeleteParent_ThrowsException`) all pass against a live Spanner Omni + PGAdapter container in well under a second each (no Spanner-specific skip needed or added) — root cause was never identified, so treat this as resolved-by-environment (a PGAdapter/Spanner Omni image update, most likely) rather than a pengdows.crud fix; if it resurfaces, re-open investigation from scratch rather than assuming the old analysis above still applies.

22. **Constraint-kind classification (Unique/ForeignKey/NotNull/Check) is unified: `IDbExceptionTranslator.Translate` now takes the `ISqlDialect` and delegates to its `IsXxxViolation` overrides instead of re-deriving SqlState/error-code matches independently.** This closed a real, proven divergence, not a hypothetical one: `SqliteExceptionTranslator` used to classify Unique violations via a bare `message.Contains("UNIQUE")`, while `SqliteDialect.IsUniqueViolation` correctly required SQLite's extended result codes (1555/2067) or the specific phrase `"UNIQUE constraint failed"` — a message like `"cannot create UNIQUE INDEX on table..."` (an unrelated DDL error) was misclassified as a unique-constraint violation by the translator while the dialect correctly said no (see `SqliteTranslatorTests.MessageWithIncidentalUniqueSubstring_AgreesWithDialectClassification`). Before delegating the other 8 `DbException`-shaped translators (Postgres/SqlServer/MySql/DuckDB/Oracle/Firebird/Db2/Snowflake), each was checked signal-by-signal against its dialect's overrides — two genuinely needed **widening first** (not narrowing — the dialect became the superset, so no existing correct behavior was lost): `MySqlDialect.IsCheckConstraintViolation` gained the translator's message-pattern fallback (`"constraint"` + `"failed for"`, for MariaDB's message-only shape with no numeric code), and `Db2Dialect`'s four overrides gained the translator's numeric-SQLCODE-magnitude fallback (`Math.Abs(errorCode) == 803/407/545/530-532`) for when IBM.Data.Db2's `DB2Exception` doesn't populate SqlState anywhere at all. **`SybaseExceptionTranslator` is deliberately excluded from this delegation** — `SybaseDialect` only overrides the `Exception`-typed `IsUniqueViolation`, not the other three (which would silently fall back to `ISqlDialect`'s `false`-returning defaults, since `AseException` isn't a `DbException` at all), so Sybase keeps its own complete, correct, distinct-error-code-per-constraint-type classification until/unless its dialect surface is deliberately widened to match. When adding a new database: write its constraint-kind logic ONCE, on the dialect (`IsUniqueViolation` etc.) — the translator should just call it, not re-derive the same signal in a parallel switch, unless the new provider's exception isn't a `DbException` (Sybase's situation), in which case document why like `SybaseExceptionTranslator`'s remarks do.
    - **This delegation itself introduced a real regression that only live verification caught, reinforcing item 21's point about this session's own changes, not just new databases.** `SqlServerDialect.IsForeignKeyViolation` required the message to contain `"FOREIGN KEY"`, matching every existing unit-test fixture — but a live SQL Server container's real DELETE-blocked-by-child-row message reads `"The DELETE statement conflicted with the REFERENCE constraint ..."`, with no "FOREIGN KEY" wording at all (only INSERT/UPDATE-blocked-by-missing-parent uses that phrasing). The old `SqlServerExceptionTranslator` never actually checked the message for FK violations — it unconditionally assumed FK for any 547 that wasn't CHECK — so this real message shape was accidentally handled correctly before, and broke silently once delegated to the narrower dialect check. Every unit test stayed green through this; only re-running `ConstraintViolationTests` against real containers (`INTEGRATION_ONLY=SqlServer,PostgreSql,...`) surfaced it, as `ForeignKeyViolation_DeleteParent_ThrowsException` regressing from 14/14 to 13/14. Fixed by widening `IsForeignKeyViolation` to accept either phrasing. A hand-written unit-test fixture suite only proves internal consistency with itself — any change to constraint-classification logic needs a live re-run against real containers before being trusted.
23. **A third, previously-undocumented exception classifier exists, separate from the two in item 11: `SqlContainer.ClassifyTranslatedException`** — a small hand-written switch pattern-matching the *already-typed* `DatabaseException` subtype (`DeadlockException`→`Deadlock`, `ConstraintViolationException`→`ConstraintViolation`, `CommandTimeoutException`→`Timeout`, else `Unknown`) purely to bucket a metrics counter after `IDbExceptionTranslator.Translate` has already produced the real exception. It runs in the *opposite direction* from items 11/22 (typed-exception → category, not raw-exception → typed-exception) so it can't disagree with the dialect the way the translator used to — but it's a third place that knows about the exception hierarchy's shape, and a new `DatabaseException` subclass added without a matching arm here silently falls into `Unknown` for metrics purposes only (never affects what gets thrown or caught). Check this switch too when adding a new exception type to the hierarchy, not just the translator/dialect pair.
24. **A dialect's `GetSupportedIsolationLevels(bool)` return set and "which `IsolationLevel` the real ADO.NET driver actually throws `InvalidOperationException` for" are two different axes — don't assume the first predicts the second.** Investigated while looking at `testbed/TestProvider.cs`'s `TestInvalidIsolationLevels`, which hardcodes one "known unsupported" `IsolationLevel` per database: for 5 of 9 databases with a hardcoded case (Oracle, CockroachDb, DuckDB, TiDb, Snowflake), `GetSupportedIsolationLevels(false)` is missing MORE THAN ONE level, yet the hardcoded test targets one *specific* one that isn't simply "the first missing." E.g. Oracle's supported set is `{ReadCommitted, Serializable}` (both `ReadUncommitted` and `RepeatableRead` are absent), but the test specifically requests `RepeatableRead`; CockroachDb/DuckDB support only `{Serializable}` (three levels absent) but the test specifically requests `ReadCommitted`. This means some drivers silently coerce/accept a semantically-unsupported level instead of throwing for it, and only one specific level per database was ever empirically confirmed (presumably against a live container) to actually throw — `GetSupportedIsolationLevels`'s logical/semantic completeness is not a reliable predictor of a live driver's actual rejection behavior. A mechanical "pick any level absent from `GetSupportedIsolationLevels`" refactor here was investigated and **deliberately not made** — it would have silently replaced 5 validated fixtures with unverified guesses. If you need to determine which isolation level a database's driver actually rejects, verify it against a live container; do not derive it from the supported-levels set.
25. **Not every `SupportedDatabase` switch outside `dialects/` is duplicated dialect knowledge — some are dialect-agnostic by design and shouldn't be centralized.** Investigated during this pass: the interval/spatial type converters (`IntervalYearMonthConverter`, `IntervalDaySecondConverter`, `PostgreSqlIntervalConverter`, `SpatialConverter` in `pengdows.crud/types/converters/`) each switch on `SupportedDatabase` to pick a wire format, but reach that switch only through `AdvancedTypeRegistry`/`CoercionRegistry`'s deliberately dialect-agnostic, independently-testable shared-singleton contracts (`IAdvancedTypeConverter`) — no `ISqlDialect` reference exists anywhere in that call chain, and there's no existing dialect member this duplicates. Centralizing these onto the dialect would mean threading an `ISqlDialect` instance through those shared contracts' public shape, a real interface change, for knowledge that isn't actually duplicated anywhere today — left alone. Separately, `ProviderParameterFactory`'s `ApplyBooleanNormalization`/`ApplyMySqlOptimizations` bypass the already-existing `SqlDialect.BooleanDbType` virtual member and re-hardcode MySql/MariaDb's byte-storage choice instead of consulting it — a real, legitimate future candidate, but **not** acted on in this pass: fixing it means threading a dialect instance into the same kind of deliberately-static, provider-agnostic write-path utility as the interval/spatial converters above (see the "Parameter type-coercion layering" note below on why that layer is handled cautiously), and doing so needs its own dedicated verification pass, not a drive-by fix bundled into a constraint-classification cleanup.
26. **DDL column-type-name lookup ("what's the BIGINT/DATETIME/BOOLEAN type name for this provider's `CREATE TABLE`") is deliberately kept OFF `ISqlDialect` — it's a test-only concern with zero production callers.** Investigated: `pengdows.crud` has no schema/DDL-generation feature for entities at all (the only adjacent tool, `pengdows.poco.mint`, goes the other direction — schema-to-POCO, not POCO-to-DDL) — this need is 100% confined to test infrastructure building small scratch tables. Adding a `GetDdlTypeName`-style member to `ISqlDialect` would add public API surface (triggering the `interface-api-check` baseline) for a concern the repo's own "minimize public APIs" rule argues against serving zero real callers. `pengdows.crud.IntegrationTests`' `IntegrationObjectNameHelper` (item 19) already consolidates this for that project; a second, older, independent copy of the same idea also lives in `testbed/TestProvider.cs` (`GetDateTimeType`/`GetIntType`/`GetLongType`/`GetBooleanType`/`GetDecimalType`/`GetBinaryType`/`GetTextType`/`GetGuidType`/`GetDateTimeOffsetType`) — left un-unified with `IntegrationObjectNameHelper` deliberately: the two live in separate test projects with no shared reference today, and no drift/bug has ever been found between them (unlike the DDL-whole-template duplication in `TestTableCreator.cs`/`TypeHydrationTableCreator.cs`, which item 19 already flags as a distinct, coarser problem). If a real drift is ever found between `testbed`'s and `IntegrationObjectNameHelper`'s type-name tables, that's the trigger to revisit unification — not the mere existence of two copies.

27. **A "PostgreSQL wire-protocol-compatible" database is not a "same type system as PostgreSQL" database — verify every non-trivial column type and constraint form live, even ones that look completely standard.** A full-suite live run against Spanner surfaced five distinct, previously-undiscovered type/constraint gaps beyond item 17's `TIMESTAMP` finding, all confirmed against a real Spanner Omni + PGAdapter instance:
    - **`NUMERIC`/`DECIMAL` with a precision/scale modifier is rejected outright** — `"P0001: Type modifier is not supported for type <numeric>."` Spanner's `NUMERIC` is fixed-precision; declare it bare. Fixed in `IntegrationObjectNameHelper.DecimalType`, `TypeHydrationTableCreator.CreateSpannerSql`, and `TestTableCreator`'s round-trip-table Spanner branch (which needed splitting out of the shared PostgreSQL/CockroachDb/YugabyteDb case it was riding — see item 29 for how that call site was found).
    - **`SMALLINT`/`int2` doesn't exist at all** — `"P0001: Type <int2> is not supported; use bigint or int8 instead."` Fixed by using `BIGINT` for every Spanner column that would otherwise be `SMALLINT`.
    - **A `uuid` DDL column type is accepted at `CREATE TABLE` time but Npgsql can't actually bind a parameter as that type** — `"The NpgsqlDbType 'Uuid' isn't present in your database."` Spanner never registered a real `uuid` type Npgsql recognizes, despite accepting the DDL text. Fixed with a genuine **production code** change: `SpannerDialect.GuidFormat => GuidStorageFormat.String` (Spanner previously inherited `PostgreSqlDialect`'s `PassThrough`, which was simply wrong for this provider) — see `GuidStorageFormatTests.Spanner_Guid_ConvertsToString_HyphenatedFormat`. DDL columns changed to `VARCHAR(36)` to match.
    - **An inline table-level `UNIQUE (...)` constraint in `CREATE TABLE` is rejected outright** — `"P0001: <UNIQUE> constraint is not supported, create a unique index instead."` Fixed with two new shared helpers, `IntegrationObjectNameHelper.InlineUniqueConstraintClause`/`SpannerUniqueIndexSql` (empty clause + a separate `CREATE UNIQUE INDEX` statement for Spanner, the normal inline clause for everyone else), applied in `MergeConflictTests`/`CompositeKeyTests`. This has a matching **cleanup-side** consequence: `DatabaseSchemaHelper`'s shared per-test `DROP TABLE` cleanup then failed with `"Cannot drop table ... with indices: ..."` for every subsequent test once a table with one of these indices existed — fixed generically (parses the index name(s) straight out of Spanner's own error message and drops them first) rather than hardcoding the one table/index pair, so it covers any future Spanner unique index without further changes here.
    - **An identifier containing a space is rejected even when correctly double-quoted** — `"P0001: ... table name not valid: Default Order."` Unlike every other type/constraint gap above, this is a genuine Spanner identifier-naming-rule limitation, not a workaround-able SQL shape difference — real PostgreSQL (and every other provider this suite runs against) accepts a quoted identifier containing a space without complaint. `QuotingTortureTests` now skips Spanner explicitly (both table setup and the test body) with the reason documented in-line, rather than trying to construct a Spanner-safe "evil identifier" that would test something materially weaker than what this test exists to verify.
28. **`SupportsBatchUpdate` (PostgreSqlDialect's optimized `UPDATE ... FROM (VALUES (...)) AS s(...)` pattern) doesn't reliably inherit down to a PostgreSQL-wire-compatible subclass — verify it live, don't assume a shared base dialect's SQL-generation capability transfers.** Confirmed live: Spanner (inheriting `PostgreSqlDialect.SupportsBatchUpdate => true`) fails a batch update with `"42883: operator does not exist: bigint = text"` — Spanner's query planner doesn't infer the VALUES-derived table's column types from the joined key column the way real PostgreSQL does, so the untyped VALUES parameter stays `text` and the comparison to a typed key column is rejected. Fixed by overriding `SpannerDialect.SupportsBatchUpdate => false`, which makes `TableGateway.BuildBatchUpdate` fall back to one `BuildUpdate` container per entity — the same safe, already-proven fallback path SQLite/MySQL/MariaDB/Firebird use for the same flag.
29. **When a DDL-building test class has both a same-named-sounding standalone helper class AND its own private per-provider method, check which one the failing test actually calls before fixing either.** `pengdows.crud.IntegrationTests/Infrastructure/RoundTripTableCreator.cs` (a whole class, `CreateTableAsync()`) and `TestTableCreator.CreateRoundTripTableAsync()` (a private method on a different, more heavily-used class) both build the exact same `round_trip_entity` table, independently, for the exact same purpose — but `RoundTripTests.cs` only ever calls the latter. A first fix pass landed correctly-written Spanner-specific DDL in `RoundTripTableCreator.cs`, verified it compiled, and moved on — but the live test kept failing with the exact same error, because that class has zero callers anywhere in the codebase. Confirmed dead via a full-repo grep and deleted rather than left as a confusing, misleading second copy. Grep for the entity/table name across the whole test project before trusting that a plausibly-named creator class is the one actually wired to the failing test.
30. **`INSERT ... ON CONFLICT` targeting a non-primary-key unique index may not be implemented on Spanner even though the primary-key-conflict case is — not yet fully confirmed, verify live before trusting `SupportsInsertOnConflict => true` in every shape.** `SpannerDialectTests.SupportsInsertOnConflict_IsTrue` documents `ON CONFLICT (id) DO UPDATE`/`DO NOTHING` (conflicting on the primary key) as verified live and working. But `MergeConflictTests.MergeRecord_UpsertAfterRemoteChange_ProducesCombinedValue` — which conflicts on `record_key`, a *secondary* unique index (the one item 27 already documents needing a separate `CREATE UNIQUE INDEX` since inline `UNIQUE` is rejected) — failed with `"P0001: io.grpc.StatusRuntimeException: UNIMPLEMENTED"` on the `INSERT` itself. This is a plausible, real distinction (Spanner's storage model centers on primary-key ranges; secondary indexes are separately-maintained structures, and conflict detection against one may genuinely not be implemented the same way), but this session did not re-verify it after the secondary-unique-index fix landed — **left open, not confirmed either way.** If you hit this on a future Spanner-adjacent database, test `ON CONFLICT` against a secondary unique index specifically, don't assume the primary-key case generalizes.

31. **SAP HANA added.** `HanaDialect`/`HanaExceptionTranslator`/detection tokens/fakeDb schema fixture all landed, LIVE-VERIFIED end-to-end against a real `saplabs/hanaexpress` 2.00.088.00 container run by hand via Docker using both `hdbsql` and the real `Sap.Data.Hana.Net.v8.0` 2.29.27 ADO.NET driver — see `HanaDialect.cs`'s file-level summary for the full trail. A `HanaTestContainer`/`HanaTestProvider` were added in a follow-up pass (opt-in, `INCLUDE_SAPHANA=true` — see the Docker/CI note below) and confirmed live: full CRUD lifecycle and stored-procedure execution pass; ~2-3 minute container spinup even with the image already pulled locally. Headline dialect findings, several of which contradicted the pre-research guess this note used to make:
    - **Isolation levels:** all four of ReadUncommitted/ReadCommitted/RepeatableRead/Serializable are accepted without error by `HanaConnection.BeginTransaction` (Snapshot is correctly rejected) — broader than the originally-guessed snapshot-only set. Whether ReadUncommitted delivers genuine dirty reads server-side, vs. a silent upgrade, was not verified (would need a second concurrent session).
    - **Pagination:** `LIMIT n OFFSET m` confirmed working; the SQL:2008 `OFFSET n ROWS FETCH NEXT m ROWS ONLY` form confirmed rejected — matching the pre-research guess.
    - **MERGE:** works, but only with a `USING (SELECT ... FROM DUMMY) s` source (Oracle/DUAL-shaped) — the base `USING (VALUES (...)) AS s (...)` row-constructor shape is rejected, same failure mode as Informix. Required a `RenderMergeSource` override.
    - **Generated keys — a real hazard caught by existing test infrastructure, not by research:** `SELECT CURRENT_IDENTITY_VALUE() FROM DUMMY` works correctly immediately after INSERT on a single held connection, and the instinctive move is to wire it up as `GeneratedKeyPlan.SessionScopedFunction`. `GeneratedKeyPlanReachabilityTests.cs` (CORE-016/TEST-010) exists specifically to catch this: `SessionScopedFunction` is a primary, non-fallback plan in `TableGateway.Core.cs`'s default path, and a session-scoped function is not guaranteed to land on the same *pooled* physical connection that ran the INSERT. `CompoundStatement` (MySQL's fix for the identical hazard) was tried and confirmed rejected live — HANA has no multi-statement command support at all. Left at the base class's default (`CorrelationToken`) instead of overriding `GetGeneratedKeyPlan()`.
    - **Exception classification:** `HanaException.SqlState` is an empty string for every violation kind except unique, and `HanaException.ErrorCode` is *always* the generic COM HRESULT `-2147467259` — both confirmed live to be useless for classification. Only `HanaException.NativeError` carries the real code (301/461/462/287/677), found automatically via the shared `TryGetProviderErrorCode` reflection helper's existing `"NativeError"` probe, with zero dialect-specific reflection needed.
    - **Docker/CI:** `HanaTestContainer`/`HanaTestProvider` are wired into `ParallelTestOrchestrator` but deliberately kept out of the always-on testbed matrix. `saplabs/hanaexpress` is a real, pullable image (~4.5GB), but a working container needs 16-32GB RAM per SAP's own guidance — far beyond a standard CI runner and beyond every other testbed container's footprint. This is a second, resource-based opt-in category alongside Snowflake's credentials-based one, gated behind `INCLUDE_SAPHANA=true` (same shape as `INCLUDE_SNOWFLAKE`). Not part of `deploy.yml`'s CI run for the same resource reason; run it manually via `INCLUDE_SAPHANA=true dotnet run --project testbed -- --only "SAP HANA"`.

32. **InterBase added.** `InterBaseDialect`/`InterBaseExceptionTranslator`/detection tokens/fakeDb schema fixture/`InterBaseDialectTests.cs` all landed and LIVE-VERIFIED end-to-end against a real InterBase 15.1.0.42 server running in Docker (a node-locked Developer Edition license registered to the container's static IP, `~/prj/interbase/`), using the real `InterBaseSql.Data.InterBaseClient` 10.0.3 ADO.NET driver. A full `InterBaseTestContainer`/`InterBaseTestProvider` pass followed and was run live to completion: **29 checks passed, 0 failed, 9 skipped**, with several real bugs found and fixed along the way (see below) — matches the "run it, and fix any issues" pattern established for SAP HANA.
    - **Two corrections to this session's own EARLIER research**, caught only by re-verifying live rather than trusting prior notes: (1) savepoints were previously reported broken ("GENERATOR SP1" error) — re-tested with an actual before/after row-survival check (insert, savepoint, insert, rollback-to-savepoint, insert, commit, then verify exactly the right rows survived) and savepoints work completely correctly, full `Create`/`Rollback`/`Release` set. (2) NOT NULL and CHECK violations were previously reported sharing one ErrorCode (335544558, Firebird-style) — re-verified live that they're numerically DISTINCT (NOT NULL is 335544347); no message-text discrimination needed.
    - **BIGINT does not exist in InterBase 15 at all** — confirmed live via the testbed's own DDL failing with SQLCODE -607 "Specified domain or source column ... does not exist" (a domain-lookup failure, not a syntax error, since the parser doesn't recognize BIGINT/INT64 as type keywords). `NUMERIC(18,0)` is InterBase's classic 64-bit-range idiom (`TestProvider.GetLongType`). Also found the same way: InterBase has no DATETIME type either (use TIMESTAMP) — but DOES have a native BOOLEAN and ordinary INT/VARCHAR, contrary to a naive Firebird-parity assumption.
    - **`GeneratedKeyPlan.PrefetchSequence`, exercised live for the first time by any shipped dialect:** InterBase supports none of IDENTITY columns, `CREATE SEQUENCE`, or `INSERT ... RETURNING` (confirmed live, SQLCODE -104 each), so `InterBaseDialect.GetGeneratedKeyPlan()` explicitly overrides to `PrefetchSequence`, using the classic InterBase 6 `CREATE GENERATOR` + `GEN_ID(name, 1)` pair. Because `TableGateway.Core.cs`'s `PrefetchSequence` branch unconditionally overwrites the entity's `[Id]` with the generator value — even though `TestTable.Id` is a client-writable `[Id]`, not `[Id(false)]` — this surfaced two genuine, generalizable bugs in the testbed's own shared `TestProvider.cs` (not InterBase-specific hacks): `TestRowRoundTrip`/`TestGetOrdinalUnknownColumnBehavior` were looking up an inserted row by the pre-insert local `id` variable instead of the entity's actual post-insert `t.Id`, which happened to always match for every OTHER dialect's key plan but silently diverges for `PrefetchSequence`. Fixed to use `t.Id` — a no-op for every other database, a real fix for InterBase.
    - **`TestErrorMapping`'s duplicate-PK check cannot work as written against `PrefetchSequence`** — reusing the same entity object for two `CreateAsync` calls to force a PK collision doesn't work when every `CreateAsync` call unconditionally fetches a fresh generator value, so the two inserts never actually collide. `InterBaseTestProvider` overrides this one test with a raw-SQL INSERT (explicit, repeated id, bypassing the generator entirely) to force a genuine violation — a legitimate per-database override (matches the checklist's "override TestUpsertCapability() etc. only when the database has a documented limitation" allowance), not a `SupportedDatabase` switch in shared code.
    - **`CREATE GENERATOR`/`GEN_ID` identifier case must match exactly**, first found live: an unquoted `CREATE GENERATOR test_table_seq` folds to uppercase, while `InterBaseDialect.GetSequenceNextValQuery`'s `GEN_ID({WrapObjectName(...)}, 1)` quotes and preserves lowercase — a real case mismatch producing "generator test_table_seq is not defined" until `InterBaseTestProvider.CreateTable()`'s own generator DDL was quoted to match.
    - **`DROP TABLE IF EXISTS` silently masks stale state on a persistent (non-ephemeral) test database**: `TestProvider.CreateTable()`'s base DROP uses "IF EXISTS", which InterBase rejects outright and the base method's own try/catch swallows — meaning a table left over from a prior run against this container (unlike every other testbed database's fresh-container-per-run model) never actually gets dropped, and the next `CREATE TABLE` fails with "already exists". `InterBaseTestProvider.CreateTable()` pre-drops with InterBase's real bare `DROP TABLE` syntax first.
    - **A genuinely surprising, non-ANSI-standard finding**: a CHECK constraint on a nullable column REJECTS a NULL value outright (standard SQL treats `chk > 0` against NULL as UNKNOWN, which a CHECK constraint normally treats as passing) — a schema-design fact for callers, not a dialect capability-flag change.
    - **BLOB columns cannot be used in a WHERE `=` comparison at all** ("feature is not supported: BLOB and array data types are not supported for compare operation") — a genuine InterBase engine restriction NOT shared by Firebird despite both using "BLOB" as their binary column type; `TestProvider.SupportsBinaryParameterBinding` gained an InterBase case (skips the compare-based binary check, same practical effect as Informix's own binary restriction there for a different underlying reason).
    - **A FOURTH correction, prompted by a user report** that InterBase 15's documented type catalog is the older, pre-Firebird-4/5 set: `INT128` and `DECFLOAT` both fail with the exact same SQLCODE -607 "Specified domain or source column ... does not exist" signature already found for `BIGINT`/`INT64` (confirming none of these are recognized type keywords at all, not a coincidence specific to one name), and `TIMESTAMP WITH TIME ZONE`/`TIME WITH TIME ZONE` both fail as genuine syntax errors (SQLCODE -104) — InterBase has no time-zone-aware temporal type whatsoever. Chasing this down surfaced a real, previously-undiscovered gap: binding a raw `DbType.DateTimeOffset` parameter with no coercion fails at the DRIVER level ("Invalid data type: 27") — the same real limitation `FirebirdSql.Data.FirebirdClient` has for the identical reason. Fixed with the Firebird-equivalent `InterBaseDialect.CreateDbParameter` override (coerce to UTC `DateTime`, `DateTimeKind.Unspecified`) — confirmed live, through the FULL pengdows.crud stack (not just the raw driver), to round-trip correctly. `TestProvider.SupportsDateTimeOffsetBinding`/`GetDateTimeOffsetType` gained InterBase cases; the live testbed re-run went from 27→29 checks passed, 11→9 skipped.
    - **Docker/CI — a THIRD, distinct opt-in category**: `InterBaseTestContainer` deliberately does NOT spin up a fresh container via Testcontainers like every other database here — InterBase's Developer Edition license binds to a machine id derived from the container's IP at one-time GUI registration, and that registration state lives in a persistent named Docker volume (`~/prj/interbase/docker-compose.yml`), not the image, so a fresh anonymous container would have no license at all. It instead connects to an already-running, externally/manually-managed container (best-effort `docker start` convenience only), gated behind `INCLUDE_INTERBASE=true`. A second, host-side (not container-side) requirement: `InterBaseSql.Data.InterBaseClient` P/Invokes a native `libgds.so` that must be resolvable on whatever machine runs the testbed process itself — confirmed as a real gap on this session's own research sandbox, worked around there with a local `LD_LIBRARY_PATH` shim (not part of production code). Not part of `deploy.yml`'s CI run, for both the licensing and native-library reasons; run it manually via `INCLUDE_INTERBASE=true dotnet run --project testbed -- --only "InterBase"` on a machine with the container already running and `libgds.so` resolvable.

33. **YDB investigated and DELIBERATELY REJECTED — not a driver-maturity gap, a real ADO.NET contract violation.** `Ydb.Sdk` 0.35.0 (`Ydb.Sdk.Ado.YdbProviderFactory`) is a genuine, mature ADO.NET provider — LIVE-VERIFIED against a real `ydbplatform/local-ydb` container: real `DbProviderFactory`/`GetSchema`, named `$` parameters, backtick-quoted always-case-sensitive identifiers (even unquoted — contradicting the driver's own self-reported `IdentifierCase=Insensitive`), a working `INSERT ... RETURNING`, a native multi-row `UPSERT INTO` statement, and `YdbDbType.Uuid` round-tripping a native `Guid` with no byte-order conversion. All of that was built into a full `YdbDialect`/`YdbExceptionTranslator`/fakeDb fixture/unit-test pass (91/91 green) and a `YdbTestContainer`/`YdbTestProvider` pair, and got as far as passing `CREATE TABLE` live before hitting the actual blocker: **`YdbCommand.ExecuteNonQuery()` and `DbDataReader.RecordsAffected` return `-1` (ADO.NET's "unknown" sentinel) for every DML statement** — INSERT, UPDATE, DELETE, and UPSERT alike, confirmed live via a real container even though the write genuinely happened (`SELECT COUNT(*)` afterward confirms it). This is not a syntax quirk a dialect override can absorb — `TableGateway.Core.cs`'s default insert path and `TableGateway.Upsert.cs` both hard-code `rowsAffected == 1`/`!= 0`-style checks as their sole success signal, so against this driver `CreateAsync`/`UpdateAsync`/`DeleteAsync`/`UpsertAsync` would ALL report failure (and `CreateAsync` would incorrectly restore audit fields) on writes that actually succeeded — a correctness-affecting framework gap, not a cosmetic one. Properly fixing it means a new cross-cutting dialect capability threaded through every affected-row-count check in the core write path — a real feature, not a per-database addition. **Explicitly declined by the user rather than building that fix or shipping a documented partial (read/DDL-only) dialect** — the entire addition (enum value, dialect, translator, detection tokens, fakeDb fixture, unit tests, testbed container/provider, every registration site) was reverted in the same session it was built, verified back to the exact pre-YDB green baseline (8434/8434 unit tests). **Before ever reattempting YDB**: re-verify this exact behavior first — the driver situation described here is what blocked it, not lack of research; if `Ydb.Sdk` ever starts reporting real affected-row counts, or `pengdows.crud` grows the cross-cutting capability described above, this is worth revisiting, but nothing else has changed.

34. **`testbed/DatabaseTypeCatalog.cs` added — a structured, per-database column-type reference/discovery catalog, prompted by a user-provided type-catalog audit covering Db2, FlatFile, SingleStore, Sybase ASE, Spanner (both dialects), Informix, SAP HANA, and InterBase.** This is deliberately NEW, ADDITIVE test infrastructure — it does not replace or get consumed by `testbed/TestProvider.cs`'s existing `GetIntType`/`GetLongType`/`GetBooleanType`/`GetDateTimeType`/`GetTextType`/`GetDecimalType`/`GetBinaryType`/`GetGuidType`/`GetDateTimeOffsetType` helpers (item 26's rationale for keeping DDL type-name lookup off `ISqlDialect` still applies unchanged; migrating those call sites to consume this catalog is real, separate follow-up work, not attempted here). A flat `name -> DbType` dictionary was rejected as insufficient — `ColumnTypeDescriptor` carries `Category`/`CanDeclareColumn`/`CanReturnFromQuery`/`IsUserDefined`/`IsArray`/`IsLob`/`IsBinary`/`IsTemporal`/`HasTimeZone`/`IsUnsigned`/`IsAutoGenerated`/`MinVersion`/`MaxVersion`/`Notes` — proven necessary by two real cases now locked down by `DatabaseTypeCatalogTests.cs`: Sybase ASE's predefined `timestamp` UDT looks temporal by name but is an automatically maintained binary row-version value (`Category = RowVersion`, `IsTemporal = false`); Spanner PostgreSQL's `interval` really is temporal but `CanDeclareColumn = false` (query-only). Populated ONLY for the 7 databases with sourced/cited or live-verified data (Spanner: PostgreSQL dialect only — pengdows.crud has no GoogleSQL-dialect Spanner support, so that half of the audit isn't modeled); every other already-supported database returns an empty list rather than a guess, by design (`GetColumnTypes_UnpopulatedDatabase_ReturnsEmptyRatherThanThrowing` locks this down) — extend with the same sourcing discipline, not by assumption from a "similar" engine.
    - **A real discrepancy surfaced and resolved, not "fixed": SAP HANA.** The audit's HANA Cloud (QRC 2/2026) type list has no `DATETIME` at all (only DATE/TIME/SECONDDATE/TIMESTAMP), which looked like it should have broken `TestProvider.GetDateTimeType`'s HANA behavior (no override exists there — it falls through to the base default, literally `"DATETIME"`). Before touching any code, this session's own saved HANA testbed logs (`hana_testbed_run.log`/`run2.log`, captured earlier this session) were checked directly rather than re-assumed from a conversation summary or re-running the (16-32GB, minutes-long) container unnecessarily: both show `Create table: OK` against a real `saplabs/hanaexpress` container using exactly that `DATETIME` column type. Resolution: this dialect targets HANA's on-prem/Express edition, not the "HANA Cloud" product line the audit's primary source describes — the older/on-prem edition accepts a compatibility alias the newer Cloud docs simply don't list. `DatabaseTypeCatalog.cs`'s HANA section documents this explicitly and warns against "fixing" `GetDateTimeType` based on the catalog alone. Lesson: when a live-verified fact and a cited documentation source disagree, check for a product-edition/version mismatch before assuming either one is wrong — and check your own prior session's saved logs before deciding a live re-verification is necessary.

35. **`TESTBED_REUSE_CONTAINERS=true` — opt-in container sharing across the net8.0/net10.0 testhost processes of one `dotnet test` run, wired into `run-integration-tests.sh`.** `pengdows.crud.IntegrationTests` targets `net8.0;net10.0`, and a single `dotnet test` invocation on it launches two FULLY SEPARATE testhost processes that each independently stand up the whole `IntegrationTestFixture` (confirmed live: both `VSTest version` banners print within the same second) — meaning every provider's container gets pulled/booted twice for identical test content, a major (measured ~65 min/framework) contributor to the suite's ~1.4-hour total runtime. `TestContainerReuse.cs` (new, in `testbed/`) fixes this for the 7 providers where per-run database isolation is cheap and mechanical — `PostgreSqlTestContainer`, `MySqlTestContainer`, `MariaDbContainer`, `SqlServerTestContainer`, `CockroachDbTestContainer`, `YugabyteTestContainer`, `TiDBTestContainer` — via three pieces: (1) `.WithReuse(true)` on the `ContainerBuilder`, using ONLY config that's identical across both processes (image, fixed bootstrap-database env vars) — a value that were randomized per-instance would make the two processes compute different reuse hashes and never recognize each other's container; (2) a cross-process `FileStream`-based advisory lock (`TestContainerReuse.AcquireStartupLockAsync`) wrapped around the `IContainer.StartAsync()` call specifically — REQUIRED, not optional: `WithReuse`'s hash-matching only recognizes an ALREADY-RUNNING container, it has no locking of its own, so two processes starting at the same instant (confirmed live to be the real, not theoretical, case) both find nothing and both create their own container without this; (3) a randomly-named database created inside the shared container per `ITestContainer` instance (not per container) and dropped on teardown, so two processes sharing one physical container never collide on table names — genuinely necessary, since `IntegrationObjectNameHelper.Table()` and most test files use fixed literal table names (`test_table`, `versioned_jobs`, `returning_test`, etc.) with no per-run uniqueness of their own. The explicit `container.DisposeAsync()` call in each class's teardown is skipped when reuse is enabled (Testcontainers' own Ryuk-based cleanup is already disabled by `WithReuse`, but the harness's own explicit dispose call is not — without this guard, whichever process finishes first would kill the container out from under the other). Verified live end-to-end multiple ways: the testbed's own 24-database/version matrix still passes unmodified (866 checks) with the random-per-instance database in place; a real concurrent `dotnet test` run (not a synthetic race) showed both processes creating separate containers WITHOUT the startup lock and sharing exactly one WITH it; a full-fixture run (all 12 default providers) completed with all 7 reuse-enabled providers appearing as single shared containers and zero cross-run failures. **Left unfixed, deliberately scoped out**: Oracle, Db2, Sybase, Informix, SAP HANA, InterBase, Firebird, Spanner — each either bootstraps a new logical database more expensively (Oracle/Db2 users vs. a cheap `CREATE DATABASE`), uses a single pinned image with its own startup fragility (Sybase's SIGSEGV workaround), or isn't a shared Testcontainers-managed resource at all (SQLite/DuckDB/FlatFile are per-process-local already; InterBase connects to an externally-managed container). Extending this pattern to those is real, separate follow-up work — the same three-piece pattern applies, but each needs its own verification of what "cheap per-run isolation" looks like for that engine. Containers left running after a reuse-enabled run are expected (that's the point) and harmless; `docker container prune` clears them, or leave them for the next run to reuse again.

36. **Read/write pool separation — four mechanisms, checked in this exact priority order, and none of them safe to assume without live verification.** Every new client-server dialect needs a real, driver-confirmed answer for how its reader and writer connections get separated into distinct physical ADO.NET pools, plus whether read-only is genuinely enforced anywhere below `SqlContainer`'s own pre-flight check. This was a real, systemic gap: `AccessDialect` originally inherited every one of `SqlDialect`'s defaults for this unexamined, and one of them (`PoolingSettingName => "Pooling"`) was an outright bug — Access's real OLE DB driver doesn't recognize the generic ADO.NET "Pooling" keyword at all and rejected it with a misleading, unrelated-looking error (`"Could not find installable ISAM"`) on *every* `DbMode.Standard` connection. A full audit later found the same class of gap in Db2, Sybase ASE, Informix, InterBase, SAP HANA, and even the long-established Firebird dialect — see `docs/connection/new-database-pooling-appname-readonly-audit.md` for the complete, worked, per-database trail this item summarizes. Priority order (stop at the first one that's real and confirmed; implement it, then still verify the ones below independently since they're separate concerns):
    1. **A real connection-string-level read-only property** (`ISqlDialect.GetReadOnlyConnectionParameter()`) — the best outcome, since a distinct property both enforces read-only at connect time AND differentiates the reader connection string from the writer's, achieving pool separation for free. `AccessDialect`'s `"Mode=Read"` and `FlatFileDialect`'s `"readonly=true"` are confirmed, worked examples.
    2. **`ApplicationNameSettingName`** — the next-best fallback when no read-only property exists: doesn't enforce anything itself, but differentiates the pool key (`ConnectionPoolingConfiguration.ApplyApplicationNameSuffix`, called unconditionally from `BuildReaderConnectionString`) and makes logs/monitoring views readable. Verify the *real* keyword via reflection against the actual driver assembly (dump every property off a live-instantiated `DbConnectionStringBuilder`-derived type) — don't assume a generic spelling like `"ApplicationName"` is recognized without checking; Db2's real keyword is `"ClientApplicationName"` (a lower-level `ProgramName` also exists but doesn't surface in `SYSIBMADM.APPLICATIONS`), and several drivers (Informix, InterBase, Firebird — before this fix, SAP HANA) have no such keyword at all.
    3. **`ReadOnlyPoolDiscriminatorSettingName`/`Value`** — the fallback of last resort when neither of the above exists (`BuildReaderConnectionString` only applies this when `ApplicationNameSettingName` is empty). This is where most of the real subtlety lives:
       - **Prefer a candidate value that equals the driver's own already-implicit compiled-in default** (`InterBaseDialect`'s `"fetch size"="200"`, SAP HANA's `"ConnectionTimeout"="15"`, Informix's `"LeaveTrailingSpaces"="False"`) — this is guaranteed behaviorally inert *by construction*, a stronger bar than merely observing a candidate "connects without error and looks unchanged across a few runs" (`AccessDialect`'s `"Jet OLEDB:Database Locking Mode"="1"` and `OracleDialect`'s `"Metadata Pooling"="false"` predate this stronger bar and were verified the weaker way instead — don't downgrade to that standard for a new dialect when the stronger one is achievable).
       - **Test the candidate through the actual production code path** (`ConnectionPoolingConfiguration.ApplyPoolDiscriminator`'s generic `DbConnectionStringBuilder`), not the dialect's own typed connection-string builder. SAP HANA's investigation found these can give opposite answers: `HanaConnectionStringBuilder` silently *omits* any property explicitly set to its own default value from the serialized connection string — meaning a candidate chosen and "confirmed" via the typed builder can be completely invisible to the real pool key, a false negative that would ship a discriminator that discriminates nothing.
       - **Avoid a property a real caller is plausible to have already set themselves.** `ApplyPoolDiscriminator` skips setting the key at all if it's already present in the caller's own connection string (`builder.ContainsKey(discriminatorSettingName)`) — silently defeating pool separation in exactly the case where a caller has customized that setting. Informix's investigation found three equally "default-matching" candidates (`Exclusive=no`, `MaxPoolSize=100`, `LeaveTrailingSpaces=False`) and deliberately rejected `MaxPoolSize` for this reason, picking the more obscure `LeaveTrailingSpaces` instead.
       - **A property literally named `IsReadOnly`/similar on the driver's builder is a trap, not a candidate** — confirmed independently on both `InterBaseSql.Data.InterBaseClient.IBConnectionStringBuilder` and `FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder`: it's `System.Data.Common.DbConnectionStringBuilder`'s own *inherited* "is this builder instance locked" indicator (no setter, no effect on the serialized connection string), not a real provider-specific keyword. Check `DeclaredOnly` reflection (or the actual declaring type) before trusting a promisingly-named property.
    4. **`TryEnterReadOnlyTransaction`/`TryEnterReadOnlyTransactionAsync`** (via the shared `SqlDialect.TryExecuteReadOnlySql`/`TryExecuteReadOnlySqlAsync` helper `OracleDialect`/`InformixDialect`/`HanaDialect` all use) — session/transaction-level SQL, the correct fallback *only* when no connection-string-level property exists. **Never assume the ANSI `SET TRANSACTION READ ONLY` pattern is portable** — it's a flat syntax error on both Db2 and Sybase ASE, while it genuinely works on Oracle/Informix/SAP HANA; verify live per-database from scratch every time, in either direction. If it works, also check whether the flag is **sticky across `COMMIT`** — SAP HANA's is: a read-only transaction's flag silently persisted into the *next* transaction on the same pooled connection, a real pooled-connection-hygiene hazard (checklist item 12) that required a `GetBaseSessionSettings()` override issuing `"SET TRANSACTION READ WRITE"` as a per-checkout reset. Separately, some drivers expose a genuine, confirmed-working read-only transaction mechanism that still can't be wired into this hook at all: InterBase's `IBTransactionOptions`/Firebird's `FbTransactionOptions` both require the read-only flag at transaction-*creation* time (a distinct `BeginTransaction(options)` overload), not as SQL run after the fact — `TryEnterReadOnlyTransaction` only ever runs after `TransactionContext` has already opened the transaction via the ordinary `IsolationLevel`-based path, so this class of mechanism is a currently-tracked, genuinely-open gap (needs a new pengdows.crud extension point letting a dialect control transaction *creation* itself) rather than something to force with a broken partial implementation — leaving it undone and documented is the correct outcome, not a failure.
    5. **`SupportsExternalPooling`/`PoolingSettingName`** — verify the real `Pooling`-equivalent keyword too, even though most drivers surveyed (Db2, Sybase ASE, Informix, InterBase, SAP HANA, Firebird) happened to match the `SqlDialect` base default (`"Pooling"`) exactly. Access was the one confirmed exception (no such keyword recognized at all — `SupportsExternalPooling => false`, matching the embedded-engine pattern DuckDB/FlatFile already use), and it's exactly the kind of silent, provider-specific mismatch this whole item exists to catch — don't skip verifying it just because most providers happen to agree.
    6. **`MaxPoolSizeSettingName`/`MinPoolSizeSettingName`** — verify these are actually wired up, not just that the underlying keyword exists. `Db2Dialect` had the real keyword (`"Max Pool Size"`/`"Min Pool Size"`, confirmed via reflection) but never set these two properties at all, so `PoolingConfigReader` silently ignored any explicit `Max Pool Size` a caller wrote into their own Db2 connection string and always fell back to the dialect default — a real, previously-undiscovered bug, not a documentation gap. Separately, don't assume a driver enforces whatever pool-size value ends up in the connection string at all: Db2's driver was confirmed live to enforce `Max Pool Size` as a real cap at *no* value (0, 5, 100, or unset all behaved identically — 150 concurrent opens succeeded in ~0.01s with `Max Pool Size=5` explicitly set, cross-checked against `SYSIBMADM.APPLICATIONS` showing 313 genuine concurrent server-side sessions) — for a driver like this, pengdows.crud's own in-process `PoolGovernor` is the *only* real admission-control safety net, and no dialect-level "fix" is possible or needed beyond documenting the fact.

### Opt-in exceptions (require env var)

Databases that **cannot run in a standard, freely-shareable Docker container** may remain opt-in:
- `INCLUDE_SNOWFLAKE=true` — cloud-only, requires credentials
- `INCLUDE_SAPHANA=true` — real Docker image, but needs 16-32GB RAM, far beyond a standard CI runner
- `INCLUDE_INTERBASE=true` — a personal, non-shareable, node-locked Developer Edition license (registration state lives in a persistent volume, not the image) plus a native `libgds.so` required on the host running the testbed process
- `INCLUDE_ACCESS=true` — a FOURTH distinct reason: no Docker image exists at all (Access isn't a server process), and the ACE OLE DB provider plus the ADOX COM interop used to create the `.accdb` file are both Windows-only

All other databases must run automatically with no env var gating.

### Aurora variants

`AuroraMySql` and `AuroraPostgreSql` are managed AWS services with no Docker image.
They are detected at runtime via `DatabaseDetectionService` and delegate to the MySQL/PostgreSQL
dialect respectively. No separate integration suite is required; they are covered by the
MySQL/PostgreSQL suites.

## Core Invariants

1. **DatabaseContext is SINGLETON** — one per connection string
2. **TableGateway is SINGLETON** — stateless, caches compiled accessors
3. **Extend TableGateway** — put custom query methods in inherited class, not wrapper service
4. **IAuditValueResolver is SINGLETON** — must be thread-safe/AsyncLocal-based to avoid captive dependencies in singleton gateways
5. **TenantContextRegistry is SINGLETON** — manages per-tenant contexts
6. **Transactions are operation-scoped** — create inside methods, never store as fields
7. **ITrackedReader is a lease** — pins connection until disposed, dispose promptly
8. **DbMode selection/coercion is safety-first** — `Best` auto-selects; explicitly unsafe modes are coerced when required (e.g., SQLite/DuckDB `Standard` -> `SingleWriter`, LocalDB -> `PreventDatabaseUnload`)
9. **Always use WrapObjectName()** — for column names and aliases in custom SQL
10. **NEVER use TransactionScope** — incompatible with connection management, use `ctx.BeginTransaction()`
11. **ISqlContainer execution methods return ValueTask** — not Task, for reduced allocations
12. **All async methods have CancellationToken overloads** — pass tokens through for proper cancellation

## Commit & Pull Request Guidelines

- Commits: short, imperative; optional prefixes `feat:`, `fix:`, `refactor:`, `chore:`.
- PRs: clear description, rationale, scope; link issues; list behavioral/provider impacts; include tests.
- Before review: ensure `dotnet build` and `dotnet test` pass locally.

## Security & Configuration Tips

- Never commit secrets or real connection strings; use environment variables and user-secrets. Strong-name via `SNK_PATH` (do not commit keys).
- Do not hardcode identifier quoting. Use `WrapObjectName(...)` and `CompositeIdentifierSeparator` (e.g., `var full = ctx.WrapObjectName("schema") + ctx.CompositeIdentifierSeparator + ctx.WrapObjectName("table");`).
- Always parameterize values (`AddParameterWithValue`, `CreateDbParameter`); avoid string interpolation for SQL.
- `pengdows.crud.analyzers` now enforces raw predicate/join value injection as `PGC008`; `IS NULL` / `IS NOT NULL` are the normal exceptions.

## Additional Requirements

- All unit and integration tests (including `testbed` scenarios) must pass with NO skipped tests.
- Use `pengdows.crud.IntegrationTests` for database-specific behaviors; `testbed` for multi-provider verification.
- If `fakeDb` lacks needed mocking capabilities, ADD them to `fakeDb` — don't invent new mocking layers.
- When functionality is unclear, consult the wiki (`pengdows.crud.wiki/`) or ASK before proceeding.
- We are targeting **95% test coverage** across the repository. Every contribution should move us closer to that goal.
- The build pipeline enforces that coverage never drops; each run must leave the coverage percentage at least equal to the previous baseline (never lower).

## Related Projects

- **`pengdows.poco.mint`**: Code generation tool that inspects a database schema and generates C# POCOs with the correct `[Table]`, `[Column]`, `[Id]`, and `[PrimaryKey]` attributes for use with `pengdows.crud`.
- **`pengdows.crud.fakeDb`**: Standalone NuGet package providing a fake ADO.NET provider. Essential for fast, isolated unit tests for any data access logic based on ADO.NET interfaces, including code that uses `pengdows.crud` or Dapper.
- **`pengdows.stormgate`** / **`pengdows.stormgate.EntityFrameworkCore`**: Sibling packages, not wired into `DatabaseContext`'s own connection governance. A lightweight ADO.NET connection admission controller (gates concurrent connection *opens*) for existing Dapper/EF Core/raw-ADO.NET applications that aren't migrating to pengdows.crud — not a substitute for `DbMode.SingleWriter`, which solves a different problem (write serialization on already-open connections). See `pengdows.stormgate/README.md`.
- **`pengdows.crud.opentelemetry`**: OpenTelemetry metrics adapter bridging `MetricsUpdated` into `System.Diagnostics.Metrics`. See `docs/opentelemetry-metrics.md`.

## AI Agent Files

This repository contains guidance files for multiple AI coding assistants:
- `CLAUDE.md` — Claude Code
- `AGENTS.md` — OpenAI Codex / Agents (this file)
- `GEMINI.md` — Google Gemini
- `skills/claude/` — Claude Code skills (slash commands)
- `skills/codex/` — Codex agent references

All three guidance files share the same core technical information.
