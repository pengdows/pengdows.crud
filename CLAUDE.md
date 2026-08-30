# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

## Project Overview


pengdows.crud 2.0 is a SQL-first, strongly-typed, testable data access layer for .NET 8. The project consists of multiple components:

- `pengdows.crud` - Core library with TableGateway, DatabaseContext, and SQL dialects
- `pengdows.crud.abstractions` - Interfaces and enums (all public APIs live here)
- `pengdows.crud.fakeDb` - A complete .NET DbProvider for mocking low-level calls
- `pengdows.crud.Tests` - Comprehensive unit test suite
- `pengdows.crud.IntegrationTests` - Database-specific integration tests
- `testbed` - Integration testing with real databases via Testcontainers
- `benchmarks/CrudBenchmarks/` - BenchmarkDotNet suite for performance validation
- `tools/` - Utilities (interface-api-check, verify-novendor, run-tests-in-container.sh)

## Breaking Changes from 1.0

- `EntityHelper<TEntity, TRowID>` renamed to `TableGateway<TEntity, TRowID>`
- Interface-first design mandate: all public APIs exposed through `pengdows.crud.abstractions`
- Several key interfaces refactored (see abstractions project)
- API baseline enforcement via `tools/interface-api-check`
- Separated integration tests into dedicated project
- All hot-path execution methods return `ValueTask` (not `Task`)

## Core Architecture

The library follows an interface-first, layered architecture:

### Main Entry Points
- **DatabaseContext** (`IDatabaseContext`): Primary connection management class wrapping ADO.NET DbProviderFactory
- **TableGateway<TEntity, TRowID>** (`ITableGateway<TEntity, TRowID>`): Generic CRUD operations for entities with strongly-typed row IDs
- **SqlContainer** (`ISqlContainer`): SQL query builder with parameterization support

### Three-Tier API (TableGateway)

**Tier 1 — Build methods** (SQL generation only, no execution):
Return `ISqlContainer`; nothing sent to the database. You inspect, modify, or execute the container yourself.

```csharp
ISqlContainer BuildCreate(entity);
ISqlContainer BuildBaseRetrieve("alias");   // SELECT with no WHERE — starting point for custom queries
ISqlContainer BuildRetrieve(ids, "alias");  // SELECT ... WHERE id IN (...)
ISqlContainer BuildRetrieve(entities, "a"); // SELECT ... WHERE pk columns match
ISqlContainer BuildDelete(id);
ISqlContainer BuildUpsert(entity);
ISqlContainer sc = await BuildUpdateAsync(entity);  // Only async Build method
```

**Tier 2 — Load methods** (execute a pre-built container, map results):
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
List<TEntity> list = await RetrieveAsync(ids); // auto-chunks into multiple round trips if ids.Count exceeds MaxParameterLimit on a dialect without set-valued parameters — see docs/parameter-naming-convention.md
IAsyncEnumerable<TEntity> stream = RetrieveStreamAsync(ids);
```

### Three-Tier API (PrimaryKeyTableGateway)

`PrimaryKeyTableGateway<TEntity>` (`IPrimaryKeyTableGateway<TEntity>`) is for entities identified **solely by `[PrimaryKey]` columns** with **no surrogate `[Id]` column**. Use it for junction tables, legacy schemas, and DBA-owned tables with natural keys.

**Throws `InvalidOperationException` at construction** if the entity has no `[PrimaryKey]` columns.

**Tier 1 — Build methods:**
```csharp
ISqlContainer BuildCreate(entity);
ISqlContainer BuildBaseRetrieve("alias");           // SELECT with no WHERE
ISqlContainer BuildRetrieve(entityList, "alias");   // SELECT ... WHERE pk columns match
ISqlContainer BuildUpsert(entity);
ISqlContainer sc = await BuildUpdateAsync(entity);  // Only async Build method
IReadOnlyList<ISqlContainer> BuildBatchCreate(entities);
IReadOnlyList<ISqlContainer> BuildBatchUpdate(entities);
IReadOnlyList<ISqlContainer> BuildBatchUpsert(entities);
IReadOnlyList<ISqlContainer> BuildBatchDelete(entities);
```

**Tier 2 — Load methods** (same as TableGateway):
```csharp
TEntity? result                  = await LoadSingleAsync(container);
List<TEntity> list               = await LoadListAsync(container);
IAsyncEnumerable<TEntity> stream = LoadStreamAsync(container);
```

**Tier 3 — Convenience methods:**
```csharp
bool created  = await CreateAsync(entity);
TEntity? e    = await RetrieveOneAsync(entityLookup); // By [PrimaryKey] only
int affected  = await UpdateAsync(entity);
int affected  = await DeleteAsync(entityCollection);  // No DeleteAsync(id) — batch only
int affected  = await UpsertAsync(entity);
// Batch shortcuts (also accept IReadOnlyList<TEntity>):
int affected  = await BatchCreateAsync(entities);
int affected  = await BatchUpdateAsync(entities);
int affected  = await BatchUpsertAsync(entities);
int affected  = await BatchDeleteAsync(entityCollection);
```

**Key differences from `TableGateway<TEntity, TRowID>`:**
- No `TRowID` type parameter — all WHERE clauses use `[PrimaryKey]` columns
- No `DeleteAsync(id)` / `BuildDelete(id)` — only entity-collection delete
- No `RetrieveAsync(ids)` / `RetrieveStreamAsync(ids)` — retrieve by entity list
- `loadOriginal` overload exists for API symmetry but is always ignored

**Example — junction table with composite natural key:**
```csharp
[Table("order_items")]
public class OrderItem
{
    [PrimaryKey(1)]
    [Column("order_id")] public int OrderId { get; set; }

    [PrimaryKey(2)]
    [Column("product_id")] public int ProductId { get; set; }

    [Column("quantity")] public int Quantity { get; set; }
    [Column("unit_price")] public decimal UnitPrice { get; set; }
}

var gateway = new PrimaryKeyTableGateway<OrderItem>(context);
await gateway.CreateAsync(new OrderItem { OrderId = 1, ProductId = 42, Quantity = 3, UnitPrice = 9.99m });
var item = await gateway.RetrieveOneAsync(new OrderItem { OrderId = 1, ProductId = 42 });
await gateway.BatchDeleteAsync(new[] { item });
```

### Key Patterns
- Program to interfaces; concrete types satisfy contracts in `pengdows.crud.abstractions`
- Entities use attributes for table/column mapping (`[Table]`, `[Column]`, `[Id]`, `[PrimaryKey]`)
- Audit fields via `[CreatedBy]`/`[CreatedOn]`, `[LastUpdatedBy]`/`[LastUpdatedOn]` attributes
- SQL dialect abstraction supports 16 databases (SQL Server, PostgreSQL, MySQL, MariaDB, Oracle, SQLite, DuckDB, Firebird, CockroachDB, YugabyteDB, TiDB, Snowflake, Aurora MySQL, Aurora PostgreSQL, TimescaleDB, Db2)
- Connection strategies: Standard, PreventDatabaseUnload, SingleWriter, SingleConnection (`KeepAlive` is an obsolete compatibility alias)
- Multi-tenancy via context-per-tenant (not query filtering)

## Development Commands

**IMPORTANT**: All unit tests must pass and all integration tests (testbed) must pass. No tests may be skipped. CI enforces minimum **83% coverage**; target **95%** for new work.

### Build and Test
```bash
# Build entire solution
dotnet build pengdows.crud.sln -c Release

# Run all tests
dotnet test -c Release --results-directory TestResults --logger trx

# Run specific test by name
dotnet test --filter "MethodName=TestMethodName"

# Run tests for specific class
dotnet test --filter "ClassName=TableGatewayTests"

# Test with coverage (CI-like)
dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"

# Run integration suite (requires Docker)
dotnet run -c Release --project testbed

# Verify API baseline (run after any interface changes)
dotnet run --project tools/interface-api-check/InterfaceApiCheck.csproj -c Release -- \
  --generate \
  --baseline pengdows.crud.abstractions/ApiBaseline/interfaces.txt \
  --assembly pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll

# Verify no vendor directories committed
dotnet run --project tools/verify-novendor
```

### Package Management
```bash
dotnet restore
dotnet pack pengdows.crud/pengdows.crud.csproj -c Release
dotnet pack pengdows.crud.abstractions/pengdows.crud.abstractions.csproj -c Release
dotnet pack pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj -c Release
```

## Coding Style & Naming Conventions

- C# 12 on `net8.0`; `Nullable` and `ImplicitUsings` enabled.
- File-scoped namespaces; keep lowercase namespaces (`pengdows.crud.*`).
- Indentation: 4 spaces; follow existing brace style; prefer expression-bodied members when clearer.
- Minimize public APIs; make types/members `internal` when possible. `WarningsAsErrors=true`.
- Organize by domain folders: `attributes/`, `dialects/`, `connection/`, `threading/`, `exceptions/`.
- Refer to the test mock package as `fakeDb` (lowercase f, uppercase D) in paths/docs.

## API Visibility Principles

- Program to interfaces whenever possible; concrete types exist only to satisfy the interface contracts.
- Consumers should depend on abstractions in `pengdows.crud.abstractions`.
- `ITableGateway`, `IDatabaseContext`, `ISqlContainer`, `ISqlDialect` etc. are the official surface area.
- Hide implementation details as `internal` by default.
- Prefer factory/DI creation where possible. Public constructors are allowed for core entry points (`DatabaseContext`, `TableGateway<,>`, tenant helpers) and should remain deliberate/documented.

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
7. **Choosing the gateway:** entity has `[Id]` → use `TableGateway<TEntity, TRowID>`; entity has only `[PrimaryKey]` (no `[Id]`) → use `PrimaryKeyTableGateway<TEntity>`

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

**SQL Server note:** Attempting to insert into an IDENTITY column throws unless `SET IDENTITY_INSERT ON`.

## Version Column (Optimistic Concurrency)

```csharp
[Version]
[Column("version")]
public int Version { get; set; }
```

| Operation | Behavior |
|-----------|----------|
| **Create** | If version is null/0, automatically set to 1 |
| **Update** | Increments version by 1 in SET clause; adds `WHERE version = @currentVersion` |

**Conflict detection:** `UpdateAsync` automatically throws `ConcurrencyConflictException` when a `[Version]` column is present and the UPDATE affects 0 rows (version mismatch or row deleted by another process).

## Upsert Behavior

**`TableGateway<T,TId>`** — determines conflict key as:
1. **Primary choice:** `[PrimaryKey]` columns (if any defined)
2. **Fallback:** `[Id]` column ONLY if writable (`[Id(true)]` or `[Id]`)
3. **Error:** Throws if no `[PrimaryKey]` AND `[Id]` is not writable (`[Id(false)]`)

**`PrimaryKeyTableGateway<T>`** — always uses `[PrimaryKey]` columns as the conflict key. Throws `NotSupportedException` if the entity has no updateable non-key columns (pure junction table with only PK columns), unless the dialect supports pure-key upsert (Firebird).

**SQL generated depends on database:**
- SQL Server/Oracle: `MERGE`
- PostgreSQL: `INSERT ... ON CONFLICT`
- MySQL/MariaDB: `INSERT ... ON DUPLICATE KEY UPDATE`

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
- `IAuditValueResolver.Resolve()` is synchronous only — no async overload exists. A resolver backed by an async-only identity source (e.g. a remote claims service) must block on it itself. If `Resolve()` throws, the exception propagates unwrapped from `CreateAsync`/`UpdateAsync` — not caught or translated.

**SECURITY: `AuditCreationPolicy` controls whether a caller-supplied `CreatedBy`/`CreatedOn` on the entity can override the resolver — check this before binding a request model directly onto an audited entity.**

`ITableGateway`/`IPrimaryKeyTableGateway` expose a settable `AuditCreationPolicy` property, defaulting to `PreserveExplicitValues`:

| Policy | Behavior on `CreateAsync` |
|---|---|
| `PreserveExplicitValues` (default) | If the entity's `CreatedBy`/`CreatedOn` already holds a non-default value (non-empty string, non-zero numeric, non-empty `Guid`, non-default timestamp), that value is kept as-is instead of being overwritten by the resolver. Intended for imports/migrations that need to carry an original creation timestamp/author. |
| `Authoritative` | Always overwrites `CreatedBy`/`CreatedOn` with resolver-supplied values, ignoring whatever the entity already holds. |

The default's "preserve a non-default explicit value" behavior means an application that binds an incoming request DTO directly onto an entity with audit columns — without explicitly setting `AuditCreationPolicy = AuditCreationPolicy.Authoritative` — lets a caller supply their own `CreatedBy` value, which is then trusted and persisted as the actual creator. Set `Authoritative` on any gateway whose `CreateAsync` might receive an entity populated from untrusted input.

## Multi-Tenancy

pengdows.crud uses **context-per-tenant** (not query filtering):

- Each tenant gets a separate `DatabaseContext` (different connection string/database)
- **No "WHERE tenant_id = X" injection** — tenants are physically separated
- Each tenant can use a different database type (SQL Server, PostgreSQL, MySQL, etc.)
- Use `ITenantContextRegistry` as a singleton to manage per-tenant `DatabaseContext` instances
- `TenantContextRegistry` exposes `ContextCreated` and `ContextRemoved` events (`Action<IDatabaseContext>`) — fired when a context is created or disposed/invalidated

```csharp
// Pass tenant context to CRUD methods to route to tenant's database
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

## TypeMapRegistry

**Explicit registration is NOT required.** `GetTableInfo<T>()` uses `GetOrAdd` — auto-builds on first access.

```csharp
// These are all equivalent:
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

## API Reference and Patterns

**IMPORTANT:** All interfaces in `pengdows.crud.abstractions` include comprehensive XML documentation. Refer to the XML comments for complete API documentation and implementation guidance.

### ISqlContainer Key Methods

**Query Execution (all return ValueTask):**
- `ExecuteNonQueryAsync(CommandType commandType = CommandType.Text)` - Execute INSERT/UPDATE/DELETE, returns row count
- `ExecuteScalarRequiredAsync<T>(CommandType commandType = CommandType.Text)` - Execute and return required single value
- `ExecuteScalarOrNullAsync<T>(CommandType commandType = CommandType.Text)` - Execute and return nullable single value
- `TryExecuteScalarAsync<T>(CommandType commandType = CommandType.Text)` - Execute and return scalar result metadata
- `ExecuteReaderAsync(CommandType commandType = CommandType.Text)` - Execute and return ITrackedReader

**Parameter Management:**
- `AddParameterWithValue<T>(DbType type, T value)` - Add parameter, returns DbParameter
- `AddParameterWithValue<T>(string? name, DbType type, T value)` - Add named parameter
- `CreateDbParameter<T>(string? name, DbType type, T value)` - Create parameter without adding
- `AddParameter(DbParameter parameter)` - Add pre-constructed parameter

**Query Building:**
- `Query` property - StringBuilder for building SQL
- `HasWhereAppended` - Indicates if WHERE clause already exists
- `WrapObjectName(string name)` - Quote identifiers safely (handles schema and alias prefixes)
- `MakeParameterName(DbParameter dbParameter)` - Format parameter name per dialect
- `Clone()` / `Clone(IDatabaseContext)` - Reuse SQL structure with different params or context. Deep-copies every parameter into independent `DbParameter` instances — mutating a value on the clone never affects the original (or vice versa). Can also clone across a different `IDatabaseContext`/dialect to re-render the same SQL structure for a different database.
- `WrapForStoredProc(ExecutionType, bool includeParameters = true, bool captureReturn = false)` - Renders the query text (a bare procedure name) into dialect-correct call syntax. See "Calling Stored Procedures" below.

**Parameter Naming Convention:**

| Prefix | Used in | Build method(s) |
|--------|---------|-----------------|
| `i{n}` | INSERT values | `BuildCreate`, `BuildUpsert`, batch |
| `s{n}` | UPDATE SET clause | `BuildUpdateAsync`, batch |
| `w{n}` | WHERE (retrieve IN/ANY) | `BuildRetrieve` |
| `k{n}` | WHERE id/key | `BuildDelete`, `BuildUpdateAsync` WHERE id, entity lookup |
| `v{n}` | Optimistic lock version | `BuildUpdateAsync` (only if `[Version]` column exists) |
| `j{n}` | JOIN conditions | Custom SQL |
| `b{n}` | Batch row values | `BuildBatchCreate/Update/Upsert` |

Critical distinctions for `SetParameterValue()` reuse:
- `BuildRetrieve` id slot → `"w0"` with **scalar** value (not array); PostgreSQL ANY takes array
- `BuildDelete` id slot → `"k0"`
- `BuildUpdateAsync`: SET params are `s0`…`sN`; WHERE id is `k0` (key counter, independent of set counter)
- Always pass base name without database prefix: `"w0"` not `"@w0"`

See `docs/parameter-naming-convention.md` for full per-operation detail.

### Calling Stored Procedures

Two patterns, depending on whether you need a captured return value:

**Automatic (the common case):** put the bare procedure name in the query, add parameters normally (set `.Direction` on the `DbParameter` for OUT/INOUT), then execute with `CommandType.StoredProcedure`. The executor detects this and calls `WrapForStoredProc` internally before running it as text:

```csharp
using var sc = context.CreateSqlContainer("MyProc");
sc.AddParameterWithValue("id", DbType.Int32, 42);
var p = sc.AddParameterWithValue("result", DbType.String, "");
p.Direction = ParameterDirection.Output;
await sc.ExecuteNonQueryAsync(CommandType.StoredProcedure);
// p.Value now holds the OUT parameter's value
```

**Capturing a return value (SQL Server only):** use `WrapForCreateWithReturn()`/`WrapForUpdateWithReturn()`/`WrapForDeleteWithReturn()` (all equivalent — pick whichever name fits the call site) instead of the `CommandType.StoredProcedure` path:

```csharp
using var sc = context.CreateSqlContainer("dbo.ReturnFive");
var wrapped = sc.WrapForUpdateWithReturn();
sc.Clear();
sc.Query.Append(wrapped);   // "DECLARE @__ret INT; EXEC @__ret = dbo.ReturnFive; SELECT @__ret;"
var returnValue = await sc.ExecuteScalarOrNullAsync<int>();
```

`captureReturn` (what these three convenience methods set internally) only works for `ProcWrappingStyle.Exec` (SQL Server) — every other dialect throws `NotSupportedException` if you try. `ProcWrappingStyle.None` (SQLite, DuckDB) means stored procedures aren't supported at all; any stored-proc call throws `NotSupportedException` there regardless of pattern.

**Per-dialect call syntax** (`ISqlDialect.ProcWrappingStyle`):

| Style | Databases | Generated syntax |
|---|---|---|
| `Exec` | SQL Server | `EXEC proc arg1, arg2` (space-separated, not parenthesized) — output-capable parameters get an ` OUTPUT` suffix appended per-argument |
| `Call` | MySQL, MariaDB, Db2 | `CALL proc(arg1, arg2)` |
| `Oracle` | Oracle | `BEGIN proc(arg1, arg2); END;` (PL/SQL anonymous block) |
| `PostgreSQL` | PostgreSQL, CockroachDB, YugabyteDB | **`ExecutionType`-branched:** `SELECT * FROM func(args)` for `Read`, `CALL proc(args)` for `Write` (requires PostgreSQL 11+ for `CALL`) |
| `ExecuteProcedure` | Firebird | **`ExecutionType`-branched:** `SELECT * FROM proc(args)` for `Read`, `EXECUTE PROCEDURE proc(args)` for `Write`. Firebird disallows empty `()` — omitted entirely when there are no arguments. |
| `None` | SQLite, DuckDB | Unsupported — `WrapForStoredProc` throws `NotSupportedException` |

### DatabaseContext Key Methods

**Transaction Management:**
- `BeginTransaction(IsolationLevel? isolationLevel = null, ...)` - Start transaction with native isolation level
- `BeginTransaction(IsolationProfile isolationProfile, ...)` - Start transaction with portable isolation profile

**SQL Container Creation:**
- `CreateSqlContainer(string? query = null)` - Create new SQL builder

**Key Properties on IDatabaseContext:**
- `Dialect` - The `ISqlDialect` in use for this context
- `ModeLockTimeout` - Timeout for mode/transaction completion locks; `null` = wait indefinitely
- `ReaderPlanCacheSize` - Plan cache size for reader connections
- `ConnectionMode` - Which DbMode this connection uses
- `Product` - Detected database product (PostgreSQL, Oracle, etc.)
- `NumberOfOpenConnections` / `PeakOpenConnections` - Connection pool observability

### Transaction Context (ITransactionContext)

- `WasCommitted` / `WasRolledBack` / `IsCompleted` - Transaction state
- `IsolationLevel` - Current isolation level
- `Commit()` / `Rollback()` - Transaction control; throw `TransactionException` on failure
- `SavepointAsync(string name)` / `RollbackToSavepointAsync(string name)` - Savepoints
- **After a commit or rollback failure**: `IsCompleted` is `true` (the connection has been released). `Dispose` will not attempt a second rollback.

## Connection Management and DbMode

**Philosophy:** Open connections late, close them early. Respect database-specific quirks.

| Mode | Value | Use Case |
|------|-------|----------|
| `Standard` | 0 | **Production-supported** — pool per operation, client-server databases |
| `PreventDatabaseUnload` | 1 | **Production-supported lifecycle mode** — Standard connection-per-operation behavior plus one unused permit-backed sentinel per materially separate pool; automatically selected for SQL Server LocalDB and embedded Firebird |
| `SingleWriter` | 2 | **Production-supported** — file-based SQLite/DuckDB, serializes writes via turnstile governor |
| `SingleConnection` | 4 | Every read and write serializes through one pinned connection. **Not a persistence or recovery mode for `:memory:` SQLite/DuckDB**; it is intended there for tests and ephemeral scratch data. Durable Firebird embedded deployments can use it as a production-supported specialized mode. |
| `Best` | 15 | Auto-select optimal mode based on provider and connection string |

**Standard, PreventDatabaseUnload, and SingleWriter are all production-supported modes**, each for a different deployment shape: Standard for client-server databases; PreventDatabaseUnload for databases whose lifecycle benefits from a passive attachment, including SQL Server LocalDB and embedded Firebird; SingleWriter for file-based SQLite/DuckDB, with turnstile-governed write serialization. PreventDatabaseUnload sentinels never run application work, and each consumes a permit from its corresponding pool.

**`SingleConnection` against an in-memory database (`:memory:`) is not a persistence or recovery mode — it is intended for tests and ephemeral scratch data.** An in-memory database's entire content lives inside that one connection's process memory: it has no independent persistence, crash recovery, or backup, and cannot survive a process restart or dropped connection (a fresh connection to the same `:memory:` string creates a new, empty database — see `docs/connection/connection-modes.md`). No connection-repair logic can restore the lost contents. Durable embedded Firebird instead uses `PreventDatabaseUnload` under automatic selection so work can use multiple attachments.

**`PreventDatabaseUnload` has zero to do with performance or optimization.** It retains one unused, open sentinel per materially separate pool so the target engine continues to see an active attachment. The sentinel is **never used to run a command, reader, transaction, or application operation**; all work remains Standard-style ephemeral work. Sentinels consume pool permits, are lazily replaced if reported `Closed` or `Broken`, and are disposed with the context. The old name `KeepAlive` is retained only as an obsolete compatibility alias.

**The write/read `PoolGovernor` is never actually unbounded, even under `Standard` mode with zero configuration.** `MaxConcurrentReads`/`MaxConcurrentWrites` resolve, in order: explicit config value → the connection string's `Max Pool Size` → the dialect's `DefaultMaxPoolSize` (100 for most dialects, including Snowflake, since none override it) — then get clamped to a hard, non-configurable ceiling of 512 (`DatabaseContext.AbsoluteMaxPoolSize`) regardless. So "database X behaves fine under Standard mode" is, by default, a claim about ≤100 concurrent writers, not unbounded concurrency — see `docs/connection/connection-pooling.md` for the full resolution chain before treating a green test run as evidence of higher-concurrency correctness than it actually exercised.

Setting `MaxConcurrentWrites=0` promotes the context to `ReadOnly`; the writer governor and provider minimum become zero while the reader pool remains enabled.

- **SingleWriter**: The turnstile governor serializes write *tasks* (not connections) preventing database locking errors. Note: readers already queued before a writer grabs the turnstile are not displaced.
- **Best**: Automatically selects the safest and most performant `DbMode` based on the provider and connection string.

## Transactions

Transactions are **operation-scoped** — create inside methods, never store as fields.

```csharp
using var txn = Context.BeginTransaction();
try
{
    var order = await RetrieveOneAsync(orderId, txn);
    order.Status = OrderStatus.Cancelled;
    await UpdateAsync(order, txn);
    txn.Commit();
}
catch
{
    txn.Rollback();
    throw;
}

// Portable isolation profile
using var txn = Context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);

// Savepoints
await txn.SavepointAsync("checkpoint1");
await txn.RollbackToSavepointAsync("checkpoint1");
```

**CRITICAL: Do NOT use `TransactionScope`**

`TransactionScope` is incompatible with pengdows.crud's connection management. The "open late, close early" philosophy means each operation opens/closes its own connection, which causes:
1. **Distributed transaction promotion** — Second connection within `TransactionScope` promotes to MSDTC
2. **Performance overhead** — MSDTC has significant overhead; may not work in cloud environments
3. **Broken semantics** — Connections closing between operations lose transactional guarantees

Always use `Context.BeginTransaction()` which pins the connection for the transaction's lifetime.

## Exception Hierarchy

All database and framework errors surface as typed `DatabaseException` subclasses:

```
DatabaseException (abstract)                — namespace pengdows.crud.exceptions
    Properties: Database, SqlState, ErrorCode, ConstraintName, IsTransient
    InnerException: raw provider exception, always preserved
├── DatabaseOperationException              — runtime database failures
│   ├── ConstraintViolationException (abstract)
│   │   ├── UniqueConstraintViolationException
│   │   ├── ForeignKeyViolationException
│   │   ├── NotNullViolationException
│   │   └── CheckConstraintViolationException
│   ├── TransientWriteConflictException (abstract, IsTransient = true)
│   │   ├── DeadlockException
│   │   └── SerializationConflictException
│   ├── ConcurrencyConflictException        — auto-thrown by UpdateAsync on [Version] mismatch
│   ├── CommandTimeoutException             — command timed out (IsTransient = true)
│   ├── ConnectionException                 — connection-level failure
│   ├── ReadOnlyViolationException          — write attempted on a read-only SQLite/DuckDB file (see "Read-only violations" below)
│   └── TransactionException               — begin/commit/rollback failure
├── SqlGenerationException                  — entity metadata programmer error
└── DataMappingException                    — strict-mode coercion failure
```

**Throw sites:**
- `SqlGenerationException` — thrown by `TypeMapRegistry` for entity metadata errors: missing `[Table]`, empty column name, enum `DbType` not string/numeric, duplicate column names, no `[Id]`/`[PrimaryKey]`, `[PrimaryKey]` order errors, invalid `[Version]` or audit field types. Uses `SupportedDatabase.Unknown`. Fires at registration/gateway construction, never during query execution.
- `DataMappingException` — thrown by `DataReaderMapper` in strict mode when column→property coercion fails. Uses `SupportedDatabase.Unknown`. Fires during `LoadSingleAsync`, `LoadListAsync`, `LoadStreamAsync`.
- `ConnectionException` — thrown by provider translators for connection-level failures (SQL Server error codes 10053/10054/10060/233/10061, Postgres SQLSTATE 08xx, MySQL codes 1040–1044, SQLite codes 14/26).
- `TransactionException` — thrown by `TransactionContext` when begin/commit/rollback fails. After failure, `IsCompleted = true` (connection already released); `Dispose` will not attempt a second rollback.
- `ReadOnlyViolationException` — thrown only by `SqliteExceptionTranslator`/`DuckDbExceptionTranslator` (`DbExceptionTranslationSupport.CreateReadOnlyViolation`) when the provider itself rejects a write against a read-only database file (SQLite `SQLITE_READONLY`/error 8, DuckDB SQLSTATE `25006`/`access_mode=READ_ONLY`). Always constructed with `IsTransient = false`.

`OperationCanceledException` is **never** wrapped — cancellation propagates as-is.

**Not part of this hierarchy — deliberately, not by oversight:** `ModeContentionException` (a timeout waiting for the `SingleWriter`/`SingleConnection` mode lock) and `PoolSaturatedException` (a timeout waiting for a `PoolGovernor` admission slot) both extend `TimeoutException` directly, not `DatabaseException`. `ModeContentionException` carries a `Snapshot` property (`ModeContentionSnapshot`) with waiter/timeout counts; see `docs/metrics.md`. `PoolSaturatedException` carries `PoolLabel`, `PoolKeyHash`, and a `PoolStatisticsSnapshot`. **Why they're exempt:** `SqlContainer`'s exception-translation path treats any `TimeoutException`-derived exception surfacing from an actual command execution as a raw provider timeout and translates it into `CommandTimeoutException`. If either of these were `DatabaseException` subclasses (or otherwise indistinguishable from a generic timeout), that same translation path could reclassify them, and a caller specifically reacting to admission-control/mode-lock backpressure would see the wrong exception type. Staying outside the hierarchy — and outside that translation path's reach — is what lets both propagate to the caller as themselves; `InfrastructureTimeoutExceptionIdentityTests.cs` locks this contract down for both from an actual command-execution path (not just `BeginTransaction`). A `catch (DatabaseException)` will not catch either one — catch `TimeoutException` (or the specific type) if you need to react to them. `PoolForbiddenException` (below) is likewise not part of this hierarchy, for an unrelated reason (see its own entry).

**Audit field validation** still throws `InvalidOperationException` (not `SqlGenerationException`) — this is a configuration/runtime guard, not an entity metadata error.

### Read-only violations: three exception types, one marker interface

A write rejected because of read-only state is **one of three distinct exception types**, not one — each thrown from a different layer, and only one is a `DatabaseException` subclass:

| Exception | Base type | Layer / condition |
|---|---|---|
| `ReadOnlyContextException` | `NotSupportedException` | `SqlContainer` pre-flight check: the whole context is configured `ReadWriteMode.ReadOnly`. Purely local, no round trip. |
| `ReadOnlyAccessException` | `InvalidOperationException` | `TransactionContext`/`InternalConnectionAccessAssertions.AssertIsWriteConnection` pre-flight check: this specific connection or transaction was opened read-only (e.g. an `ExecutionType.Read` transaction on an otherwise-writable context), independent of the context-wide flag above. Also local, no round trip. |
| `ReadOnlyViolationException` | `DatabaseOperationException` → `DatabaseException` | Provider actually rejected a write against a read-only SQLite/DuckDB file (see throw site above). The only one of the three that is a `DatabaseException` — `catch (DatabaseException)` catches it but not the other two. |

All three implement the marker interface `IReadOnlyViolation` (`pengdows.crud.exceptions`), so a caller who only cares "was this a read-only rejection, whichever layer caught it" can write one catch block instead of three:

```csharp
catch (IReadOnlyViolation) { /* handle any of the three uniformly */ }
```

**Retry:** never valid for any of the three. `ReadOnlyViolationException` hardcodes `IsTransient = false`; the other two aren't `DatabaseException`s at all (no `IsTransient` property), but conceptually retrying doesn't help either — the context/connection/database's read-only state doesn't change between attempts of the same operation. See `docs/read-only-enforcement.md` for full detail and examples.

**Related but distinct — not a read-only violation:** `PoolForbiddenException` (`: InvalidOperationException`, `pengdows.crud.exceptions`) is thrown by `PoolGovernor.Acquire`/`AcquireAsync` when a pool configured with `MaxConcurrentWrites=0`/`MaxPoolSize=0` is accessed at all — e.g. the write pool on a context promoted to `ReadOnly`. It does **not** implement `IReadOnlyViolation`: it is an admission-control rejection (no connection slot exists for this pool), not a rejection of a write that reached a connection.

### ISqlDialect.AnalyzeException — provider-agnostic exception analysis

`ISqlDialect.AnalyzeException(Exception)` returns a `DbExceptionInfo` record with provider-neutral fields for control flow:

```csharp
var info = context.Dialect.AnalyzeException(ex);
if (info.IsRetryable) { /* retry */ }
if (info.ConstraintKind == DbConstraintKind.ForeignKey) { /* 409 response */ }
```

`DbExceptionInfo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `Category` | `DbErrorCategory` | High-level category (ConstraintViolation, Deadlock, Timeout, …) |
| `ConstraintKind` | `DbConstraintKind` | Specific constraint: None, Unique, ForeignKey, NotNull, Check, Unknown |
| `IsTransient` | `bool` | True for deadlock, serialization failure, timeout |
| `IsRetryable` | `bool` | True when the caller should generally retry |
| `ProviderErrorCode` | `int?` | Provider-specific numeric error code when available |
| `SqlState` | `string?` | SQLSTATE code when available |

`ISqlDialect` also exposes targeted boolean helpers: `IsUniqueViolation`, `IsForeignKeyViolation`, `IsNotNullViolation`, `IsCheckConstraintViolation` — all accept `DbException` and default to `false` in the interface (overridden per dialect).

## DI Lifetime Rules

| Component | Lifetime | Why |
|-----------|----------|-----|
| `DatabaseContext` | **Singleton** | Manages connection pool, metrics, DbMode state |
| `TableGateway<T,TId>` | **Singleton** | Stateless, caches compiled accessors |
| `IAuditValueResolver` | **Singleton** | Must be thread-safe/AsyncLocal-based (e.g. `IHttpContextAccessor`) |
| `ITenantContextRegistry` | **Singleton** | Manages per-tenant contexts |

## Extending TableGateway — The Correct Pattern

**Inherit from TableGateway to add custom query methods.** Do not wrap it in a separate service class.

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

## Extending PrimaryKeyTableGateway — The Correct Pattern

Same inheritance pattern as `TableGateway` — inherit to add custom query methods.

```csharp
public interface IOrderItemGateway : IPrimaryKeyTableGateway<OrderItem>
{
    Task<List<OrderItem>> GetByOrderAsync(int orderId);
}

public class OrderItemGateway : PrimaryKeyTableGateway<OrderItem>, IOrderItemGateway
{
    public OrderItemGateway(IDatabaseContext context) : base(context) { }

    public async Task<List<OrderItem>> GetByOrderAsync(int orderId)
    {
        var sc = BuildBaseRetrieve("oi");
        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("oi.order_id"));
        sc.Query.Append(" = ");
        var p = sc.AddParameterWithValue("oid", DbType.Int32, orderId);
        sc.Query.Append(sc.MakeParameterName(p));
        return await LoadListAsync(sc);
    }
}
```

## Common Test Patterns

**Creating Test Context with FakeDb:**
```csharp
var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
var helper = new TableGateway<TestEntity, long>(context);
```

**Testing SQL Execution:**
```csharp
using var container = context.CreateSqlContainer("SELECT 1");
var result = await container.ExecuteScalarRequiredAsync<int>();
```

**Testing CRUD Operations:**
```csharp
var helper = new TableGateway<TestEntity, int>(context);
var entity = new TestEntity { Name = "Test" };
var createContainer = helper.BuildCreate(entity);
await createContainer.ExecuteNonQueryAsync();

var updateContainer = await helper.BuildUpdateAsync(entity);
var rowsAffected = await updateContainer.ExecuteNonQueryAsync();

var rowsDeleted = await helper.DeleteAsync(entity.Id);
var entities = await helper.RetrieveAsync(new[] { 1, 2, 3 });

// Custom SQL
var sc = helper.BuildBaseRetrieve("a");
sc.Query.Append(" WHERE a.Name = ");
sc.Query.Append(sc.MakeParameterName("name"));
sc.AddParameterWithValue("name", DbType.String, "Test");
var results = await helper.LoadListAsync(sc);
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

### Testing Infrastructure
- Framework: xUnit; mocks: Moq. Name files `*Tests.cs` and mirror source namespaces.
- Prefer `pengdows.crud.fakeDb` for unit tests; avoid real DBs. Use `testbed/` for integration via Testcontainers.
- Coverage artifacts live in `TestResults/`; CI publishes Cobertura from `TestResults/**/coverage.cobertura.xml`.
- The entire unit-test suite currently finishes in under 30 seconds; if a run approaches three minutes, terminate it and investigate for locking/hanging issues.
- CI enforces minimum **83% coverage**; target **95%** for new work.
- Expand `fakeDb` when tests need behaviors it lacks — don't bypass its limitations.

## Adding a New Database

**Every new database added to `SupportedDatabase` requires a complete integration test suite.** No exceptions.

**Get `MaxSupportedStandard`/`ProductInfo.StandardCompliance` right before anything else in this checklist.** ~20 of `ISqlDialect`'s `Supports*` capability flags (`SupportsJoins`, `SupportsMerge`, `SupportsWindowFunctions`, `SupportsJsonTypes`, `SupportsTemporalData`, etc.) are all derived from one comparison — `MaxSupportedStandard >= SqlStandardLevel.SqlXXXX` — in the base `SqlDialect` class (see `docs/architecture.md`'s "Capability Flags Derive From One Enum" section). An accurate standard-compliance year gets most of them correct automatically; then verify each derived flag against the engine's *actual* behavior and override individually where it diverges (some engines claim more standard compliance than they actually implement — see the YugabyteDB MERGE example on the wiki's Database-Specific Gotchas page).

### Checklist

1. **Enum value** — add to `pengdows.crud.abstractions/enums/SupportedDatabase.cs`
2. **Dialect** — create `pengdows.crud/dialects/<Name>Dialect.cs`, register in `SqlDialectFactory.cs`
3. **Test container** — create `testbed/<Name>/<Name>TestContainer.cs` (start, get context, dispose)
4. **Test provider** — create `testbed/<Name>/<Name>TestProvider.cs` (override `CreateTable()`; override `TestUpsertCapability()` etc. only when the database has a documented limitation)
5. **Always-on registration** — add to the `configurations` list in `ParallelTestOrchestrator.GetTestConfigurations()` (not in an opt-in block)
6. **Unit tests** — add dialect-level unit tests in `pengdows.crud.Tests/dialects/`

### Easy-to-miss spots (found the hard way with Db2 — check these every time)

These are places outside the dialect file itself that switch or pattern-match on `SupportedDatabase` explicitly. A new database silently falls through to a `default`/catch-all branch here instead of erroring, so nothing fails loudly — only manual review catches it.

7. **The new dialect's `GetSupportedIsolationLevels(bool)` and `GetIsolationProfileMapping(bool)` overrides** (in `pengdows.crud/dialects/<Name>Dialect.cs`, not `IsolationResolver.cs` — `IsolationResolver` only orchestrates profile resolution/degradation/validation now, it asks the dialect for the actual per-database data). Missing an override silently gives the database `SqlDialect`'s generic ANSI fallback (ReadCommitted/RepeatableRead/Serializable for every level and profile), which is wrong for any database with non-standard isolation semantics — check the isolation levels the engine genuinely enforces (not just what its SQL parser accepts; TiDB parses `SERIALIZABLE` but silently treats it as `REPEATABLE READ`) against real behavior, not assumed from a similar dialect.
8. **`DatabaseContext.Initialization.cs` → `IsClientServerDatabase()`** — add the new database if it's a real client/server RDBMS (not embedded/in-process). Missing this suppresses the diagnostic warning that's supposed to fire when someone misconfigures `SingleConnection`/`SingleWriter` mode against it.
9. **`DatabaseContext.Initialization.cs` → `CoerceMode()`** — add the new database to the explicit "full server databases" case list. If it falls to `default` instead, the *behavior* is often still correct (defaults to `Standard`), but the logged mode-override message will misleadingly say "unknown provider" for a fully-supported database.
10. **`pengdows.crud/dialects/SqlDialect.cs` → `GetNaturalKeyLookupQuery()` and any other pagination/"first row only" fallback** — check whether the new database's syntax actually matches the generic `LIMIT 1` fallback. It doesn't for Oracle (`ROWNUM = 1`), and it doesn't for Db2 (`FETCH FIRST 1 ROWS ONLY`). Grep `SqlDialect.cs` for `DatabaseType ==`/`DatabaseType !=` checks and verify each one explicitly for the new database rather than assuming the catch-all branch is correct.
11. **Exception classification (two independent systems, both need entries)** — `IDbExceptionTranslator`/`DbExceptionTranslatorRegistry` (produces the typed `DatabaseException` subclass) and `SqlDialect.TryClassifyProviderException`/`IsUniqueViolation`/`IsForeignKeyViolation`/`IsNotNullViolation`/`IsCheckConstraintViolation` (produces `DbErrorCategory`) are maintained separately and can disagree if only one is updated.
12. **`SqlDialect.GetBaseSessionSettings()` — check for session-level state that leaks across a pooled connection.** Research whether the new database has connection/session registers that (a) are not reset by transaction commit/rollback, and (b) can silently change the meaning of a subsequent caller's SQL if a prior borrower altered them (e.g. Db2's `CURRENT ISOLATION`, `CURRENT TEMPORAL SYSTEM_TIME`/`BUSINESS_TIME`). Only add settings here that are pooled-connection-hygiene invariants pengdows.crud must enforce for correctness — never application policy (default schema, lock-timeout tuning, query-optimizer knobs, application name) which belongs to the caller's own configuration, not a dialect baseline. Verify live against a real container before committing to the SQL syntax and to whether the driver accepts multiple semicolon-separated `SET` statements in one `ExecuteNonQuery` call — some drivers (Oracle) reject multi-statement batches outright and need a `BEGIN...EXECUTE IMMEDIATE...END` wrapper instead.
13. **`ISqlDialect.ProcWrappingStyle` — decide the real stored-procedure call syntax; don't leave the `SqlDialect` base default (`ProcWrappingStyle.None`) unexamined.** `None` silently means "stored procedures unsupported" with no compile error and no loud test failure — `testbed`'s stored-proc check (`TestProvider.TestStoredProcReturnValue`) just logs a skip and moves on, so a database that actually supports procedures can sail through review looking "done" while the capability is simply never implemented. This is exactly what happened with Db2: `CallProcWrappingStrategy`'s own doc comment already lists "MySQL, MariaDB, DB2" as the SQL-standard `CALL proc(args)` style it was written for, but `Db2Dialect` never actually set `ProcWrappingStyle => ProcWrappingStyle.Call` — the gap was later logged as "deliberately deferred" in project notes with no actual investigation behind that label. Pick the correct `ProcWrappingStyle` value (`Call`, `Exec`, `PostgreSQL`, `Oracle`, `ExecuteProcedure`, or a genuinely new one) by checking the database's real stored-procedure invocation syntax against `pengdows.crud/strategies/proc/*.cs`, add a case to `testbed/TestProvider.cs`'s `TestStoredProcReturnValue` switch (its `default` branch throws precisely so a new database can't be silently skipped once it's wired past `None`), and add dialect-level and integration coverage for it — don't accept `None` without writing down *why* in the dialect file.

**Next candidate: SAP HANA.** Flag its isolation-level set (HANA's default is effectively snapshot-based, not lock-based like most of the above) and its pagination syntax (`LIMIT`/`OFFSET` are supported directly, unlike Db2 — don't assume it needs a Db2-style special case, but don't assume it matches SQL Server either) for explicit research rather than copying an existing dialect's assumptions.

### Opt-in exceptions (require env var)

Only databases that **cannot run in a standard Docker container** may remain opt-in:
- `INCLUDE_SNOWFLAKE=true` — cloud-only, requires credentials

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
8. **DbMode.Best auto-selects** — SQLite `:memory:` = SingleConnection, file SQLite = SingleWriter
9. **Always use WrapObjectName()** — for column names and aliases in custom SQL
10. **NEVER use TransactionScope** — incompatible with connection management, use `Context.BeginTransaction()`
11. **Execution methods return ValueTask** — not Task, for reduced allocations
12. **All async methods have CancellationToken overloads** — pass tokens through for proper cancellation

## Security & Configuration Tips

- Never commit secrets or real connection strings; use environment variables and user-secrets. Strong-name via `SNK_PATH` (do not commit keys).
- Do not hardcode identifier quoting. Use `WrapObjectName(...)` and `CompositeIdentifierSeparator`:
  ```csharp
  var full = ctx.WrapObjectName("schema") + ctx.CompositeIdentifierSeparator + ctx.WrapObjectName("table");
  ```
- Always parameterize values (`AddParameterWithValue`, `CreateDbParameter`); avoid string interpolation for SQL.
- `pengdows.crud.analyzers` now enforces raw predicate/join value injection as `PGC008`; `IS NULL` / `IS NOT NULL` are the normal exceptions.
- `WrapObjectName` uses ANSI double-quote `"name"` identifier quoting as the **enforced default policy across every currently supported dialect** — not a different quoting syntax per database. PostgreSQL and Oracle get this natively (the `SqlDialect` base class default); SQL Server and MySQL/MariaDB each force it via their own dialect-specific session setting (`QUOTED_IDENTIFIER ON` for SQL Server, `ANSI_QUOTES` SQL mode for MySQL/MariaDB) rather than falling back to their native bracket (`[name]`) or backtick (`` `name` ``) syntax. `QuotePrefix`/`QuoteSuffix` are `virtual` on `SqlDialect` specifically so this is a policy, not a hardcoded law: a future dialect for an engine that genuinely cannot be made to accept ANSI double-quotes under any session configuration (e.g. MS Access, which has no `QUOTED_IDENTIFIER`/`ANSI_QUOTES`-style switch and doesn't support `"name"` at all) would declare its own quoting via that same override point — an explicit, declared exception, not a violation of the convention every other dialect follows.

## Commit & Pull Request Guidelines

- Commits: short, imperative; optional prefixes `feat:`, `fix:`, `refactor:`, `chore:`.
- PRs: clear description, rationale, scope; link issues; list behavioral/provider impacts; include tests.
- Before review: ensure `dotnet build` and `dotnet test` pass locally.

## Key Implementation Details

### Type Safety
- `TRowID` must be primitive integer, `Guid`, or `string` (nullable allowed)
- Automatic type coercion between .NET types and database types
- Enum support with configurable parsing behavior (string or numeric via DbType)
- JSON serialization support for complex types via `[Json]` attribute

### SQL Generation
- Database-agnostic SQL with dialect-specific optimizations
- Automatic parameterization prevents SQL injection
- Provider-specific UPSERT: MERGE (SQL Server/Oracle), ON CONFLICT (PostgreSQL), ON DUPLICATE KEY (MySQL/MariaDB)
- Schema-aware operations with proper object name quoting
- Dialect is accessible via `context.Dialect` (`ISqlDialect`) from any `IDatabaseContext`

### Advanced Features
- **Intelligent Dialect System**: Portable upsert, optimized prepared statements per database, proc wrapping per vendor
- **IsolationProfile**: Portable transaction isolation (maps to safest level for target DB)
- **Uuid7Optimized**: Built-in RFC 9562-compliant UUIDv7 generator for time-ordered, index-friendly surrogate keys
- **Comprehensive Metrics**: Connection counts, timings, pool contention, attribution stats

## Working with the Codebase

**Key Principles:**
- All async hot-path operations return `ValueTask` or `ValueTask<T>`; `await` immediately, never store
- `IDatabaseContext` parameter is often optional (defaults to instance context)
- Use `ISqlContainer` for composable SQL building
- Always dispose contexts and containers (preferably with `using`/`await using`)
- Entity classes need proper attributes: `[Table]`, `[Id]`, `[Column]`, etc.
- Program to interfaces (`IDatabaseContext`, `ITableGateway`, `ISqlContainer`)

**Common Mistakes to Avoid:**
- Don't add new public constructors unless there is a clear SDK-use reason; prefer interface-first APIs and DI/factories
- Use `RetrieveAsync` for multiple entities, `RetrieveOneAsync` for single by ID/key, `LoadSingleAsync` for custom SQL
- Use correct `ExecutionType` (Read vs Write) for connections
- Don't confuse `[Id]` (pseudo key/row ID) with `[PrimaryKey]` (business key)
- Don't store `TransactionContext` as a field — create it inside the method that uses it

## Related Projects

- **`pengdows.poco.mint`**: Code generation tool that inspects a database schema and generates C# POCOs with the correct `[Table]`, `[Column]`, `[Id]`, and `[PrimaryKey]` attributes for use with `pengdows.crud`.
- **`pengdows.crud.fakeDb`**: Standalone NuGet package providing a fake ADO.NET provider. Essential for fast, isolated unit tests for any data access logic based on ADO.NET interfaces.
- **`pengdows.stormgate`** / **`pengdows.stormgate.EntityFrameworkCore`**: Sibling packages, not wired into `DatabaseContext`'s own connection governance. A lightweight ADO.NET connection admission controller (gates concurrent connection *opens*) for existing Dapper/EF Core/raw-ADO.NET applications that aren't migrating to pengdows.crud — not a substitute for `DbMode.SingleWriter`, which solves a different problem (write serialization on already-open connections). See `pengdows.stormgate/README.md`.
- **`pengdows.crud.opentelemetry`**: OpenTelemetry metrics adapter bridging `MetricsUpdated` into `System.Diagnostics.Metrics`. See `docs/opentelemetry-metrics.md`.

## AI Agent Files

This repository contains guidance files for multiple AI coding assistants:
- `CLAUDE.md` — Claude Code (this file)
- `AGENTS.md` — OpenAI Codex / Agents
- `GEMINI.md` — Google Gemini
- `skills/claude/` — Claude Code skills (slash commands)
- `skills/codex/` — Codex agent references

All three guidance files share the same core technical information. The `skills/` directory provides structured reference material used by AI assistants during development sessions.
