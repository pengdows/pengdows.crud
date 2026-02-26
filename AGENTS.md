# Repository Guidelines

## Core Philosophy

`pengdows.crud` is an opinionated, high-performance, SQL-first data access framework built on a **database-first** philosophy. It provides **"Prego features"** — expert-level, built-in solutions to difficult real-world data access problems that developers often assume are handled by their tools but usually are not. It is designed to be more robust and feature-rich than a micro-ORM like Dapper, while retaining high performance and developer control, without the pitfalls of heavier ORMs like EF Core.

No LINQ, no tracking, no surprises — explicit SQL control with database-agnostic features.

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
- Hide implementation details as `internal` by default. **Nothing outside of `DatabaseContext` should expose a public constructor** — objects should either be resolved through factory helpers, DI, or internal constructors (exceptions require documented rationale). This keeps the SDK surface stable and highlights the interface-first programming model.

## Interface-first Mandate

- Always code against the interface contract; implementation classes exist only to fulfill the abstractions defined in `pengdows.crud.abstractions`.
- Treat interfaces such as `ITableGateway`, `IDatabaseContext`, `ISqlContainer`, `ISqlDialect`, etc. as the primary SDK surface — new code should depend on those APIs rather than concrete helpers.
- Keep concrete types internal unless a public contract is required, and do not introduce public constructors except for `DatabaseContext`.

## Interfaces & Extension Points

- `IDatabaseContext`: entry point. Create via DI or `new DatabaseContext(connStr, DbProviderFactory)`. Builds `ISqlContainer`, formats names/params, and controls connections/transactions.
  - `Dialect` property (`ISqlDialect`) — the SQL dialect in use for this context
  - `ModeLockTimeout` property (`TimeSpan?`) — timeout for mode/transaction locks; `null` = wait indefinitely
  - `ReaderPlanCacheSize` property (`int?`) — plan cache size for reader connections
- `ISqlContainer`: compose SQL safely and execute.
  Example: `var sc = ctx.CreateSqlContainer("SELECT 1"); var v = await sc.ExecuteScalarAsync<int>();`
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

**ISqlContainer execution methods return `ValueTask` (not `Task`):**
```csharp
ValueTask<int>            ExecuteNonQueryAsync(commandType);
ValueTask<T?>             ExecuteScalarAsync<T>(commandType);
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
2. TableGateway REQUIRES `[Id]` for `CreateAsync`, `UpdateAsync`, `DeleteAsync(TRowID)`
3. `[Id(false)]` = DB-generated (autoincrement); `[Id]` or `[Id(true)]` = client-provided
4. `[PrimaryKey]` defines business uniqueness, enforced via UNIQUE constraint in DDL
5. Both can coexist on different columns: pseudo key for operations, business key for domain integrity
6. `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns; `DeleteAsync(TRowID)` uses `[Id]`

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

**Conflict detection:** If `UpdateAsync` returns 0 rows affected, another process modified the row (version mismatch).

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

In `SingleWriter` mode, this determines whether you get the pinned write connection or an ephemeral read connection.

## TypeMapRegistry.Register<T>()

**Explicit registration is NOT required.** `GetTableInfo<T>()` uses `GetOrAdd` — auto-builds on first access.

```csharp
// These are equivalent:
typeMap.Register<MyEntity>();           // Explicit pre-registration
typeMap.GetTableInfo<MyEntity>();       // Auto-registers on first call
new TableGateway<MyEntity, long>(ctx);  // Also triggers auto-registration
```

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
| `Standard` | 0 | **Production default** — pool per operation |
| `KeepAlive` | 1 | Embedded DBs needing sentinel connection |
| `SingleWriter` | 2 | File-based SQLite/DuckDB — serializes writes via turnstile governor |
| `SingleConnection` | 4 | In-memory `:memory:` databases |
| `Best` | 15 | Auto-select optimal mode based on provider and connection string |

- **SingleWriter**: The turnstile governor serializes write *tasks* (not connections) preventing database locking errors. Note: readers already queued before a writer grabs the turnstile are not displaced.
- **Best**: Automatically selects the safest and most performant `DbMode` based on the provider and connection string.

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

## Core Invariants

1. **DatabaseContext is SINGLETON** — one per connection string
2. **TableGateway is SINGLETON** — stateless, caches compiled accessors
3. **Extend TableGateway** — put custom query methods in inherited class, not wrapper service
4. **IAuditValueResolver is SINGLETON** — must be thread-safe/AsyncLocal-based to avoid captive dependencies in singleton gateways
5. **TenantContextRegistry is SINGLETON** — manages per-tenant contexts
6. **Transactions are operation-scoped** — create inside methods, never store as fields
7. **ITrackedReader is a lease** — pins connection until disposed, dispose promptly
8. **DbMode.Best auto-selects** — SQLite `:memory:` = SingleConnection, file SQLite = SingleWriter
9. **Always use WrapObjectName()** — for column names and aliases in custom SQL
10. **NEVER use TransactionScope** — incompatible with connection management, use `ctx.BeginTransaction()`
11. **Execution methods return ValueTask** — not Task, for reduced allocations
12. **All async methods have CancellationToken overloads** — pass tokens through for proper cancellation

## Commit & Pull Request Guidelines

- Commits: short, imperative; optional prefixes `feat:`, `fix:`, `refactor:`, `chore:`.
- PRs: clear description, rationale, scope; link issues; list behavioral/provider impacts; include tests.
- Before review: ensure `dotnet build` and `dotnet test` pass locally.

## Security & Configuration Tips

- Never commit secrets or real connection strings; use environment variables and user-secrets. Strong-name via `SNK_PATH` (do not commit keys).
- Do not hardcode identifier quoting. Use `WrapObjectName(...)` and `CompositeIdentifierSeparator` (e.g., `var full = ctx.WrapObjectName("schema") + ctx.CompositeIdentifierSeparator + ctx.WrapObjectName("table");`).
- Always parameterize values (`AddParameterWithValue`, `CreateDbParameter`); avoid string interpolation for SQL.

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

## AI Agent Files

This repository contains guidance files for multiple AI coding assistants:
- `CLAUDE.md` — Claude Code
- `AGENTS.md` — OpenAI Codex / Agents (this file)
- `GEMINI.md` — Google Gemini
- `skills/claude/` — Claude Code skills (slash commands)
- `skills/codex/` — Codex agent references

All three guidance files share the same core technical information.
