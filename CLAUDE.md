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

# Run a driver-*version* matrix probe — distinct from testbed's server-version matrix; a fully
# separate project per driver+version pair investigated, since one project can't reference two
# versions of the same NuGet package (see FEAT-008 in docs/planning/future-work.md).
# MySql.Data versions tested against TiDB (requires Docker):
dotnet test testbed.DriverVersionMatrix/testbed.DriverVersionMatrix.csproj -c Release              # 9.7.0
dotnet test testbed.DriverVersionMatrix.MySqlData930/testbed.DriverVersionMatrix.MySqlData930.csproj -c Release  # 9.3.0
dotnet test testbed.DriverVersionMatrix.MySqlData940/testbed.DriverVersionMatrix.MySqlData940.csproj -c Release  # 9.4.0
# MySqlConnector's AllowMultipleStatements support (no Docker needed — client-side check):
dotnet test testbed.DriverVersionMatrix.MySqlConnector200/testbed.DriverVersionMatrix.MySqlConnector200.csproj -c Release  # 2.0.0
dotnet test testbed.DriverVersionMatrix.MySqlConnector262/testbed.DriverVersionMatrix.MySqlConnector262.csproj -c Release  # 2.6.2
# Oracle ODP.NET's DbType.Guid rejection (no Docker needed — client-side check):
dotnet test testbed.DriverVersionMatrix.OracleOdp321/testbed.DriverVersionMatrix.OracleOdp321.csproj -c Release  # 3.21.230 (21c line)
dotnet test testbed.DriverVersionMatrix.OracleOdp23/testbed.DriverVersionMatrix.OracleOdp23.csproj -c Release    # 23.26.300
# Npgsql's DateTimeOffset-must-be-UTC-for-timestamptz requirement, against Postgres (requires Docker):
dotnet test testbed.DriverVersionMatrix.Npgsql5/testbed.DriverVersionMatrix.Npgsql5.csproj -c Release  # 5.0.18 (pre-"6+")
dotnet test testbed.DriverVersionMatrix.Npgsql9/testbed.DriverVersionMatrix.Npgsql9.csproj -c Release  # 9.0.3
# SqlClient decimal Precision/Scale auto-inference, against SQL Server (requires Docker):
dotnet test testbed.DriverVersionMatrix.SqlClientDecimalPrecision/testbed.DriverVersionMatrix.SqlClientDecimalPrecision.csproj -c Release  # 6.0.2

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
- `SavepointAsync(string name)` / `RollbackToSavepointAsync(string name)` / `ReleaseSavepointAsync(string name)` - Savepoints; all three throw `NotSupportedException` when the dialect's `SavepointCapabilities` lacks the corresponding flag (`Create`/`Rollback`/`Release`) rather than silently no-op-ing
- **After a commit or rollback failure**: `IsCompleted` is `true` (the connection has been released). `Dispose` will not attempt a second rollback.

## Connection Management and DbMode

**Philosophy:** Open connections late, close them early. Respect database-specific quirks.

| Mode | Value | Use Case |
|------|-------|----------|
| `Standard` | 0 | **Production-supported** — pool per operation, client-server databases |
| `PreventDatabaseUnload` | 1 | **Production-supported lifecycle mode** — Standard connection-per-operation behavior plus one unused permit-backed sentinel per materially separate pool; auto-selected by `Best` **only** for SQL Server LocalDB. Available everywhere else (Firebird, Db2, SQL Server with `AUTO_CLOSE`, ...) as an explicitly-honored opt-in knob — see below for why it's deliberately not a default there. |
| `SingleWriter` | 2 | **Production-supported** — file-based SQLite/DuckDB, serializes writes via turnstile governor |
| `SingleConnection` | 4 | Every read and write serializes through one pinned connection. **Not a persistence or recovery mode for `:memory:` SQLite/DuckDB**; it is intended there for tests and ephemeral scratch data. Durable Firebird embedded deployments can use it as a production-supported specialized mode. |
| `Best` | 15 | Auto-select optimal mode based on provider and connection string |

**Standard, PreventDatabaseUnload, and SingleWriter are all production-supported modes**, each for a different deployment shape: Standard for client-server databases (this now includes Firebird — see below); PreventDatabaseUnload for databases whose lifecycle genuinely requires a passive attachment (SQL Server LocalDB) or whose operator has decided they want one; SingleWriter for file-based SQLite/DuckDB, with turnstile-governed write serialization. PreventDatabaseUnload sentinels never run application work, and each consumes a permit from its corresponding pool.

**`PreventDatabaseUnload` auto-selection is deliberately narrow: LocalDB only.** Several databases have a real, empirically-confirmed idle-triggered reconnect/reactivation cost — Firebird SuperServer's default `RDB$LINGER=0` (page cache discarded immediately after the last attachment closes — terminology note: Firebird doesn't unload the *server*, the process keeps running; only that one database's cache/attachment state unloads), Db2 LUW's implicit-activation lifecycle, SQL Server with `AUTO_CLOSE` turned on. `testbed.TestProvider.TestIdleUnloadProbe` measures this directly against live containers rather than trusting any claim (see `TryEnableFastIdleUnloadAsync`/`ClearProviderPoolForIdleUnloadProbe` and each database's overrides) — Firebird showed a consistent ~5-10x cold/warm ratio across 3.0/4.0/5.0, and a follow-up sentinel-validation step confirmed `PreventDatabaseUnload`'s actual mechanism (one connection held open) genuinely closes that gap for at least one confirmed case (SQL Server `AUTO_CLOSE`, ~68x ratio collapsed to near-warm). But **detecting a real cost does not mean `Best` should auto-select `PreventDatabaseUnload` for that database** — two considerations cut the other way: a heavily-trafficked deployment may never actually drain its connection pool to zero, so the cost never materializes while the sentinel's permit is still paid unconditionally; and a deployment deliberately built to scale-to-zero for cost reasons (the Firebird/Db2/SQL-Server case is architectural and unwanted by everyone, but genuinely cost-optimized serverless products like Azure SQL serverless or Aurora Serverless are a different animal entirely — see `docs/connection/connection-modes.md`) would have that intentional behavior silently defeated by a forced sentinel. Only the operator knows which situation applies to their deployment, so for every one of these confirmed-real-cost databases, `PreventDatabaseUnload` stays a fully-supported, explicitly-honored **knob** — never an auto-selected default — and `Best` resolves to `Standard` exactly like any other full-server database. LocalDB is the sole exception: there is no production LocalDB deployment shape where the auto-shutdown behavior is wanted, so it stays an unconditional `Best` selection (and cannot be overridden away from it at all).

**Do not casually extend `Best`'s auto-selection list to another database without the same empirical bar** — a documentation claim or a chat-message table is not sufficient evidence to change a shipped dialect's default mode resolution, and confirming a real cost exists is still not sufficient justification for auto-selecting around it (see above). A Db2 change was proposed and reverted in the same session for lacking verification; Firebird's own `Best → PreventDatabaseUnload` auto-selection was *also* built, empirically verified, and then deliberately reverted back to `Standard` once the "who decides this tradeoff" question was worked through — being empirically correct about the cost was not enough to justify the auto-selection.

**`SingleConnection` against an in-memory database (`:memory:`) is not a persistence or recovery mode — it is intended for tests and ephemeral scratch data.** An in-memory database's entire content lives inside that one connection's process memory: it has no independent persistence, crash recovery, or backup, and cannot survive a process restart or dropped connection (a fresh connection to the same `:memory:` string creates a new, empty database — see `docs/connection/connection-modes.md`). No connection-repair logic can restore the lost contents.

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

**Verify capability flags explicitly for the new database.** `ISqlDialect`'s `Supports*` capability flags (`SupportsJoins`, `SupportsMerge`, `SupportsWindowFunctions`, `SupportsJsonTypes`, `SupportsTemporalData`, etc.) are concrete boolean properties on each dialect. In the base `SqlDialect` class, universal baseline capabilities (joins, subqueries, group by, transactions) default to `true`, while advanced features (`SupportsMerge`, `SupportsWindowFunctions`, `SupportsCommonTableExpressions`, `SupportsArrayTypes`, `SupportsJsonTypes`, etc.) default to `false`. Concrete dialects override each capability explicitly with version-aware logic where appropriate (e.g., `SupportsWindowFunctions => !IsInitialized || IsVersionAtLeast(...);`). Verify each capability flag against the engine's *actual* behavior rather than assuming conformance.

### Checklist

1. **Enum value** — add to `pengdows.crud.abstractions/enums/SupportedDatabase.cs`
2. **Dialect** — create `pengdows.crud/dialects/<Name>Dialect.cs`, register in `SqlDialectFactory.cs`
3. **Test container** — create `testbed/<Name>/<Name>TestContainer.cs` (start, get context, dispose)
4. **Test provider** — create `testbed/<Name>/<Name>TestProvider.cs` (override `CreateTable()`; override `TestUpsertCapability()` etc. only when the database has a documented limitation)
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
13. **`ISqlDialect.ProcWrappingStyle` — decide the real stored-procedure call syntax; don't leave the `SqlDialect` base default (`ProcWrappingStyle.None`) unexamined.** `None` silently means "stored procedures unsupported" with no compile error and no loud test failure — `testbed`'s stored-proc check (`TestProvider.TestStoredProcReturnValue`) just logs a skip and moves on, so a database that actually supports procedures can sail through review looking "done" while the capability is simply never implemented. This is exactly what happened with Db2: `CallProcWrappingStrategy`'s own doc comment already lists "MySQL, MariaDB, DB2" as the SQL-standard `CALL proc(args)` style it was written for, but `Db2Dialect` never actually set `ProcWrappingStyle => ProcWrappingStyle.Call` — the gap was later logged as "deliberately deferred" in project notes with no actual investigation behind that label. Pick the correct `ProcWrappingStyle` value (`Call`, `Exec`, `PostgreSQL`, `Oracle`, `ExecuteProcedure`, or a genuinely new one) by checking the database's real stored-procedure invocation syntax against `pengdows.crud/strategies/proc/*.cs`, add a case to `testbed/TestProvider.cs`'s `TestStoredProcReturnValue` switch (its `default` branch throws precisely so a new database can't be silently skipped once it's wired past `None`), and add dialect-level and integration coverage for it — don't accept `None` without writing down *why* in the dialect file.
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
    - **A foreign-key-violating insert still hangs for the full command timeout (60s) before throwing `CommandTimeoutException` instead of `ForeignKeyViolationException`, reproducibly, across every FK test variant.** This is NOT a classification bug — the real underlying exception genuinely is a client-side read timeout (`TimeoutException` inside `NpgsqlException("Exception while reading from stream")`), meaning Spanner/PGAdapter itself is not responding promptly when an insert violates a foreign key. **Left open** — root cause not investigated further (whether Spanner's FK validation is asynchronous/eventually-consistent under the emulator, a PGAdapter-specific slowness, or something pengdows.crud's own connection/command handling does differently for this one case needs live protocol-level investigation this session didn't have budget for).

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

31. **SAP HANA added.** `HanaDialect`/`HanaExceptionTranslator`/detection tokens/fakeDb schema fixture all landed, LIVE-VERIFIED end-to-end against a real `saplabs/hanaexpress` 2.00.088.00 container run by hand via Docker (no reproducible testbed container yet — see below) using both `hdbsql` and the real `Sap.Data.Hana.Net.v8.0` 2.29.27 ADO.NET driver — see `HanaDialect.cs`'s file-level summary for the full trail. Headline findings, several of which contradicted the pre-research guess this note used to make:
    - **Isolation levels:** all four of ReadUncommitted/ReadCommitted/RepeatableRead/Serializable are accepted without error by `HanaConnection.BeginTransaction` (Snapshot is correctly rejected) — broader than the originally-guessed snapshot-only set. Whether ReadUncommitted delivers genuine dirty reads server-side, vs. a silent upgrade, was not verified (would need a second concurrent session).
    - **Pagination:** `LIMIT n OFFSET m` confirmed working; the SQL:2008 `OFFSET n ROWS FETCH NEXT m ROWS ONLY` form confirmed rejected — matching the pre-research guess.
    - **MERGE:** works, but only with a `USING (SELECT ... FROM DUMMY) s` source (Oracle/DUAL-shaped) — the base `USING (VALUES (...)) AS s (...)` row-constructor shape is rejected, same failure mode as Informix. Required a `RenderMergeSource` override.
    - **Generated keys — a real hazard caught by existing test infrastructure, not by research:** `SELECT CURRENT_IDENTITY_VALUE() FROM DUMMY` works correctly immediately after INSERT on a single held connection, and the instinctive move is to wire it up as `GeneratedKeyPlan.SessionScopedFunction`. `GeneratedKeyPlanReachabilityTests.cs` (CORE-016/TEST-010) exists specifically to catch this: `SessionScopedFunction` is a primary, non-fallback plan in `TableGateway.Core.cs`'s default path, and a session-scoped function is not guaranteed to land on the same *pooled* physical connection that ran the INSERT. `CompoundStatement` (MySQL's fix for the identical hazard) was tried and confirmed rejected live — HANA has no multi-statement command support at all. Left at the base class's default (`CorrelationToken`) instead of overriding `GetGeneratedKeyPlan()`.
    - **Exception classification:** `HanaException.SqlState` is an empty string for every violation kind except unique, and `HanaException.ErrorCode` is *always* the generic COM HRESULT `-2147467259` — both confirmed live to be useless for classification. Only `HanaException.NativeError` carries the real code (301/461/462/287/677), found automatically via the shared `TryGetProviderErrorCode` reflection helper's existing `"NativeError"` probe, with zero dialect-specific reflection needed.
    - **Docker/CI:** deliberately NOT wired into the always-on testbed matrix or given a testbed container/provider in this pass. `saplabs/hanaexpress` is a real, pullable image (~4.5GB), but a working container needs 16-32GB RAM per SAP's own guidance — far beyond a standard CI runner and beyond every other testbed container's footprint. This is a second, resource-based opt-in category alongside Snowflake's credentials-based one (`INCLUDE_SAPHANA=true` is documented in `docs/supported-databases.md` as the intended gate, but the testbed container/provider/orchestrator wiring itself is still open work for a future session).

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

### Parameter type-coercion layering (read this before "cleaning up" any of it)

Configuring a `DbParameter` for a given CLR value + provider is **not** one system — it's six, layered in this exact order for a WRITE (`SqlDialect.CreateDbParameter<T>`):

1. **`s_primitiveClrTypes` fast path** (`SqlDialect.cs`) — `byte`/`sbyte`/`short`/`ushort`/`int`/`uint`/`long`/`ulong`/`float`/`double`/`decimal`/`char`/`string` skip everything below entirely. `bool`/`Guid`/`DateTime`/`DateTimeOffset` are *deliberately excluded* from this set specifically so dialects can override their handling per-provider — don't "simplify" by adding them back.
2. **`AdvancedTypeRegistry`'s legacy `RegisterMapping<T>(SupportedDatabase, ProviderTypeMapping)` system** — the only mechanism that does provider-specific reflection-based enum-property setting (`NpgsqlDbType`, `OracleDbType`, `SqlDbType` via `SetEnumProperty`). Checked first; wins outright if a mapping exists for the exact `(ClrType, Provider)` pair.
3. **`CoercionRegistry`/`DbCoercion<T>`** (`ProviderParameterFactory.TryConfigureParameter`) — the general CLR↔DB value-translation layer, and the *only* one of these six with a symmetric read path (`TryRead`, used by `DataReaderMapper` for hydration) as well as write. Checked second.
4. **`ParameterBindingRules.ApplyBindingRules`** — a rule-based last-resort fallback (DateTime/bool/enum/array/large-object rules). Checked third, only if 2 and 3 both declined.
5. **`SqlDialect.ApplyGuidFormat`** — runs unconditionally whenever the caller requested `DbType.Guid` and the dialect's `GuidFormat != PassThrough`, *regardless* of whether 2-4 already handled it. This is the **real** source of truth for Guid storage on 6 of ~10 providers (Sqlite/Oracle/Snowflake/Db2/DuckDB/Firebird all override `GuidFormat`); PostgreSQL-family/SqlServer/MySQL leave it `PassThrough` and let layer 2/3 stand. **Do not try to unify Guid handling into one layer** — the `GuidFormat`-vs-`PassThrough` split across three tiers is load-bearing, not tech debt, verified during the architecture-cleanup session that added this note.
6. Two more small unconditional post-fixes directly in `CreateDbParameter`: the `_commonConversions` delegate table (Guid→string, bool→int16, DateTimeOffset→UTC, dialect-opt-in) and a hardcoded DateTimeOffset→UTC-DateTime fix for PostgreSql/Spanner/CockroachDb/YugabyteDb.

A later verification pass re-read `CreateDbParameter` line-by-line against this list and confirmed it still matches the code exactly, including layer 5's "regardless of `handled`" claim — with two small always-present steps that live in the same unconditional post-fix region as layer 6 but aren't value-coercion and so were never counted as their own layer: a `string` parameter's `Size` gets set from its length, and a `decimal` parameter's `Precision`/`Scale` gets inferred/floored at 18. Neither changes what value ends up in the parameter, only its metadata — worth knowing they're there if you're reading this method top-to-bottom expecting only the six numbered layers. Also worth noting: layers 2-4's *relative order* isn't literally inline in `CreateDbParameter` — they're all reached through one `AdvancedTypeRegistry.TryConfigureParameterForDialect` call, whose own internal short-circuit is what encodes "2 first, then 3‖4." If you ever refactor `CreateDbParameter` in isolation, that ordering lives in `AdvancedTypeRegistry.cs`, not here.

**What's genuinely dead vs. only looks dead**: layers 2 and 3 short-circuit the `||` chain before layer 4 ever runs for *most* types, so most of `ParameterBindingRules`' rules never execute in production via this one call site — but do not delete them on that basis alone. Two were investigated and kept anyway: the DateTime/bool rules in `ParameterBindingRules` are unreachable via `CreateDbParameter` today, but are a well-tested, `internal`-visible fallback API (a dozen+ existing unit tests exercise them directly) that would matter for any future caller that skips layers 2-3 — deleting them trades a real safety net for a purely cosmetic win. The legacy `RegisterMapping<RowVersion>(SqlServer, ...)` mapping and `RowVersionValueCoercion` looked write-side-dead the same way (`CreateDbParameter` converts `RowVersion`→`byte[]` before any dispatch), but `RowVersionValueCoercion.TryRead` is genuinely live — it's how a `RowVersion`-typed entity property gets hydrated back from a query result (see `ColumnInfo.cs`/`TypeMapRegistry.cs`). The same caution applies to `decimal`: it's in the primitive fast path so a `RegisterMapping<decimal>`/`ApplySqlServerOptimizations`'s Currency branch never fire on write, but `DataReaderMapper.cs`/`TypeCoercionHelper.cs` reference `decimal` on the read side — don't delete without tracing both directions. **What actually was dead and got removed**: `AdvancedTypeRegistry.TryConfigureParameterEnhanced` (a zero-caller duplicate of `TryConfigureParameterForDialect`) and `CoercionRegistry`'s provider-specific `Register<T>(SupportedDatabase, IDbCoercion<T>)` overload (nothing anywhere ever populated it) — both confirmed via a full-codebase, generics-aware grep, not just a quick pattern match (the first grep pass for the latter missed a real caller by not accounting for `Register<T>(...)`'s generic syntax — re-verify with `\.Register<[^>]*>\(SupportedDatabase\.` if you ever revisit this).

**One real bug found and fixed here**: `ProviderParameterFactory.ApplyMySqlOptimizations` set a bool parameter's `DbType` to `Byte` (TINYINT(1) compatibility) but left `parameter.Value` as a raw C# `bool` instead of `(byte)1`/`(byte)0` — see `MySqlBooleanParameterValueTests.cs`. An existing test had explicitly locked in the buggy value as "correct" (`Assert.Equal(true, parameter.Value)`); it was updated alongside the fix.

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
