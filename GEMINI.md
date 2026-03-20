# Gemini Code Assistant Context: pengdows.crud 2.0

## Mandatory Workflow And Reviews

- TDD is mandatory for every behavior change, bug fix, regression fix, and public-contract change.
- Start by writing or updating an automated test that fails for the intended reason before changing implementation.
- Do not start implementation until the test is red; after implementation, rerun the relevant automated tests and do not consider the work complete until they pass with no skipped tests introduced.
- If automated coverage is genuinely not possible, say so explicitly and document the verification gap.
- All reviews are done against [REVIEW_POLICY.md](./REVIEW_POLICY.md).
- Review output, merge guidance, blocker/major/minor classification, required evidence, and minimal patch guidance must follow [REVIEW_POLICY.md](./REVIEW_POLICY.md).
- If instructions overlap, follow the more stringent requirement. If this file conflicts with [REVIEW_POLICY.md](./REVIEW_POLICY.md) on review behavior, follow [REVIEW_POLICY.md](./REVIEW_POLICY.md).

This document provides a deep, contextual understanding of the `pengdows.crud` 2.0 framework for Gemini. It covers the project's core philosophy, its key differentiating features, and its intended use cases.

## Core Philosophy: The "Expert in a Box"

`pengdows.crud` is an opinionated, high-performance, SQL-first data access framework. It is designed to be more robust and feature-rich than a micro-ORM like Dapper, while retaining high performance and developer control, avoiding the pitfalls of heavier, abstraction-focused ORMs like EF Core.

The central design principle is to provide **"Prego features"** — expert-level, built-in solutions to difficult, real-world data access problems that developers often assume are handled by their tools but usually are not. The framework guides developers toward robust, scalable, and secure architectural patterns by default.

It is built on a **database-first** philosophy, treating the database schema as a primary, expertly-designed artifact. No LINQ, no tracking, no surprises — explicit SQL control with database-agnostic features.

## Project Structure

- `pengdows.crud/` — Core library with TableGateway, DatabaseContext, and SQL dialects
- `pengdows.crud.abstractions/` — Interfaces and enums (all public APIs live here)
- `pengdows.crud.fakeDb/` — A complete .NET DbProvider for mocking low-level calls
- `pengdows.crud.Tests/` — Comprehensive unit test suite
- `pengdows.crud.IntegrationTests/` — Database-specific integration tests
- `testbed/` — Integration testing with real databases via Testcontainers
- `benchmarks/CrudBenchmarks/` — BenchmarkDotNet suite for performance validation
- `tools/` — Utilities (interface-api-check, verify-novendor, run-tests-in-container.sh)

## Key Differentiating Features

### 1. Advanced Connection Management (`DbMode`)

The framework provides intelligent, adaptive connection strategies to ensure optimal performance and resilience.

- **"Open Late, Close Early" Architecture:** In `Standard` mode (for server databases), connections are acquired from the provider's pool only at the moment of execution and released immediately after. This maximizes connection pool efficiency and prevents pool exhaustion under high load.
- **Pool Governor:** 2.0 introduces a true pool governor for read/write slot control and fairness. It provides better protection against pool saturation, safer high-concurrency operation, and separate read and write slot budgets. It is more advanced than the standalone `pengdows.stormgate` admission controller, adding fairness and full telemetry.
- **`SingleWriter` Mode:** For file-based databases like SQLite, this mode provides a unique, built-in solution for safe concurrent writes. Re-architected in 2.0 to use a turnstile-based coordination that schedules write tasks rather than just serializing connections, significantly reducing writer starvation risk.
- **`SingleConnection` Mode:** A dedicated mode for handling thread-safe access to a single, persistent connection, designed specifically for ephemeral `:memory:` databases, which is invaluable for testing.
- **`Best` Mode:** Automatically selects the safest and most performant `DbMode` based on the provider and connection string:
    - `:memory:` SQLite/DuckDB → `SingleConnection`
    - File-based SQLite/DuckDB → `SingleWriter`
    - SQL Server LocalDB → `KeepAlive`
    - Everything else → `Standard`
- **`ModeLockTimeout`:** Configurable timeout (`TimeSpan?`) for internal mode locks and transaction completion locks; `null` means wait indefinitely.

### 2. Intelligent Dialect System

A powerful abstraction layer that makes application code portable across different database vendors by handling database-specific quirks.

- **Portable Upsert:** Automatically translates a single `Upsert` command into the correct native SQL (`MERGE`, `INSERT ... ON CONFLICT`, etc.) for the target database.
- **Intelligent Prepared Statements:** Selectively enables or disables prepared statements based on what is most performant for the target database (e.g., ON for PostgreSQL, OFF for SQL Server).
- **Native `DbDataSource` support:** `DatabaseContext` now accepts a `DbDataSource` directly (e.g., `NpgsqlDataSource`), providing shared prepared-statement caching — a significant throughput win for PostgreSQL.
- **Stored Procedure Wrapping (`ProcWrappingStyle`):** Automatically wraps stored procedure calls in the correct, vendor-specific syntax (`EXEC`, `CALL`, `BEGIN/END`, etc.).
- **`IsolationProfile`:** Portable transaction isolation profiles that map to the safest and most optimal `System.Data.IsolationLevel` for the target database (e.g., `SafeNonBlockingReads`, `StrictConsistency`, `FastWithRisks`).
- **`ISqlDialect`** is accessible directly via `context.Dialect` on any `IDatabaseContext` — no internal casts required.

### 3. Advanced Type System

A multi-layered, high-performance, and extensible type coercion system.
- Provides built-in support for advanced types like **JSON, spatial data, arrays, and network addresses**.
- **Unified GUID storage:** GUIDs are stored in the correct format for each database automatically (e.g., native UUID for Postgres, VARCHAR(36) for SQLite/Oracle, BINARY(16) for Firebird).
- Allows developers to register their own custom handlers and converters for domain-specific types, which are then used seamlessly for both parameter writing and data reading.
- All timestamps normalized to UTC; DateTime, DateTimeOffset, and TimestampOffset all supported.

### 4. Robust Database-First Design Principles

- **`[Id]` vs. `[PrimaryKey]`:** A clear distinction is made between a surrogate key (`[Id]`, for stable foreign key references) and a natural/business primary key (`[PrimaryKey]`). This encourages correct normalization and ensures that core CRUD operations can always leverage an appropriate index.
- **`Uuid7Optimized`:** Provides a high-performance, RFC 9562-compliant UUIDv7 generator to create time-ordered, index-friendly surrogate keys.

### 5. Built-in Safety and Production-Ready Patterns

- **Resource Safety:** The strict use of `IAsyncDisposable` on `TransactionContext` and `SqlContainer` makes accidental connection leaks virtually impossible.
- **Audit Handling:** An `IAuditValueResolver` interface allows for easy, decoupled, and automatic population of audit columns. **Both `CreatedBy/On` AND `LastUpdatedBy/On` are set on CREATE** — this is intentional design allowing "last modified" queries without checking if the entity was ever updated.
- **Read-only enforcement:** Dual-layer enforcement for supported databases (PostgreSQL, SQLite, DuckDB) using both connection-string-level and session SQL settings to guarantee data integrity.
- **Multi-Tenancy:** First-class support for the robust **database-per-tenant** model via `ITenantContextRegistry` — no WHERE tenant_id filtering, physical database separation.
- **NEVER use `TransactionScope`** — incompatible with the "open late, close early" philosophy. Use `context.BeginTransaction()` which pins the connection for the transaction's lifetime. 2.0 adds support for **Transaction Savepoints** on supported databases.

### 6. Comprehensive Metrics

Provides deep operational visibility by tracking 36 detailed metrics for connections, contention (from the `PoolGovernor`), command timings, transactions, and more. Metrics are collected inside the execution pipeline, providing precise observability into every connection open, slot acquisition, and command dispatch.

## Coding Style & Naming

- C# 12 on `net8.0`; `Nullable` and `ImplicitUsings` enabled.
- File-scoped namespaces; lowercase namespaces (`pengdows.crud.*`).
- Indentation: 4 spaces; `WarningsAsErrors=true`.
- Prefer factory/DI creation where possible. Public constructors are allowed for core entry points (`DatabaseContext`, `TableGateway<,>`, tenant helpers) and should remain deliberate/documented.
- Program to interfaces; concrete types exist only to satisfy `pengdows.crud.abstractions` contracts.
- Organize by domain folders: `attributes/`, `dialects/`, `connection/`, `threading/`, `exceptions/`.

## Three-Tier API (TableGateway)

**Tier 1 — Build methods** (SQL generation only, no DB I/O):
```csharp
ISqlContainer BuildCreate(entity);
ISqlContainer BuildBaseRetrieve("alias");   // SELECT with no WHERE — starting point for custom queries
ISqlContainer BuildRetrieve(ids, "alias");  // SELECT ... WHERE id IN (...)
ISqlContainer BuildDelete(id);
ISqlContainer BuildUpsert(entity);
ISqlContainer sc = await BuildUpdateAsync(entity);  // Only async Build method
// 2.0 Batch Build
IReadOnlyList<ISqlContainer> BuildBatchCreate/Update/Upsert/Delete(entities);
```

**Tier 2 — Load methods** (execute a pre-built container):
```csharp
TEntity? result                  = await LoadSingleAsync(container);
List<TEntity> list               = await LoadListAsync(container);
IAsyncEnumerable<TEntity> stream = LoadStreamAsync(container);  // 2.0 Memory-efficient streaming
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
IAsyncEnumerable<TEntity> stream = RetrieveStreamAsync(ids); // 2.0 Streaming

// 2.0 Batch Operations (chunked by MaxParameterLimit)
int affected = await BatchCreate/Update/Upsert/DeleteAsync(entities);
```


**All execution methods return `ValueTask` (not `Task`)** for reduced allocations. Clone containers for reuse: `container.Clone()` or `container.Clone(otherContext)`.

## Three-Tier API (PrimaryKeyTableGateway)

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
```

## CRITICAL: Pseudo Key (Row ID) vs Primary Key (Business Key)

**DO NOT CONFUSE THESE CONCEPTS.**

| Concept | Attribute | Columns | Purpose |
|---------|-----------|---------|---------|
| **Pseudo Key / Row ID** | `[Id]` | Always single | Surrogate identifier for TableGateway operations, FKs, easy lookup |
| **Primary Key / Business Key** | `[PrimaryKey(n)]` | Can be composite | Natural key — why the row exists in business terms |

**Key Rules:**
1. `[Id]` and `[PrimaryKey]` are MUTUALLY EXCLUSIVE on a column — never both on the same property
2. `[Id(false)]` = DB-generated (autoincrement); `[Id]` or `[Id(true)]` = client-provided
3. `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns; `DeleteAsync(TRowID)` uses `[Id]`
4. Upsert conflict key: `[PrimaryKey]` preferred; fallback to writable `[Id]`; error if neither
5. **Choosing the gateway:** entity has `[Id]` → `TableGateway<TEntity, TRowID>`; entity has only `[PrimaryKey]` → `PrimaryKeyTableGateway<TEntity>`

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

**Conflict detection:** If `UpdateAsync` returns 0 rows affected, another process modified the row (version mismatch).

## CRITICAL: Audit Field Behavior

**BOTH CreatedBy/On AND LastUpdatedBy/On are set on CREATE.**

| Operation | CreatedBy | CreatedOn | LastUpdatedBy | LastUpdatedOn |
|-----------|-----------|-----------|---------------|---------------|
| **Create** | SET | SET | SET | SET |
| **Update** | unchanged | unchanged | SET | SET |

- Entities with `[CreatedBy]` or `[LastUpdatedBy]` REQUIRE `IAuditValueResolver`.
- Without resolver + user audit fields = `InvalidOperationException` at runtime.
- The resolver ALWAYS returns UTC timestamps; DateTime, DateTimeOffset, and TimestampOffset all supported.

## Multi-Tenancy

pengdows.crud uses **context-per-tenant** (not query filtering):

- Each tenant gets a separate `DatabaseContext` (different connection string/database)
- **No "WHERE tenant_id = X" injection** — tenants are physically separated
- Each tenant can use a different database type (SQL Server, PostgreSQL, MySQL, etc.)
- Use `ITenantContextRegistry` as a singleton to manage per-tenant `DatabaseContext` instances

## ExecutionType (Read vs Write)

| Type | Intent | Connection behavior |
|------|--------|---------------------|
| `ExecutionType.Read` | Read-only operation | May get ephemeral or shared connection |
| `ExecutionType.Write` | Modifying operation | Gets write-capable connection |

## TypeMapRegistry

**Explicit registration is NOT required.** `GetTableInfo<T>()` uses `GetOrAdd` — auto-builds on first access.

## Enum Storage

| DbType | Storage |
|--------|---------|
| `DbType.String` | Stored as enum name (string) |
| Numeric (`Int32`, etc.) | Stored as underlying numeric value |

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
10. **NEVER use TransactionScope** — incompatible with connection management, use `context.BeginTransaction()`
11. **Execution methods return ValueTask** — not Task, for reduced allocations
12. **All async methods have CancellationToken overloads** — pass tokens through for proper cancellation

## Security & Configuration

- Never commit secrets or real connection strings; use environment variables and user-secrets.
- Do not hardcode identifier quoting — use `WrapObjectName(...)` and `CompositeIdentifierSeparator`.
- Always parameterize values (`AddParameterWithValue`, `CreateDbParameter`); avoid string interpolation for SQL.

## Project Mandates

### 1. Test-Driven Development (TDD) — MANDATORY

All new functionality, dialect additions, and bug fixes MUST be implemented using a TDD approach. Tests must be written and verified before or alongside the implementation.

**TDD Workflow:**
1. WRITE THE TEST FIRST — Before ANY implementation code
2. RUN THE TEST — Verify it fails (red)
3. WRITE MINIMAL IMPLEMENTATION — Just enough to pass (green)
4. REFACTOR — Improve while keeping tests green
5. REPEAT

### 2. High Coverage Standards

The project CI enforces a minimum of **83% line coverage**. However, for all new 2.0 work, a target of **95% coverage** is expected. A change is not considered complete without corresponding unit tests (in `pengdows.crud.Tests`) and, where applicable, integration tests (in `testbed`).

### 3. Interface-First Design

All public APIs must be exposed via interfaces in `pengdows.crud.abstractions`. Implementation details should remain internal to `pengdows.crud` whenever possible, and new public constructors should be introduced only for clear SDK-use scenarios.

## Building and Testing

```bash
# Build
dotnet build pengdows.crud.sln -c Release

# Unit tests
dotnet test -c Release --results-directory TestResults --logger trx

# Run specific test
dotnet test --filter "MethodName=TestMethodName"

# Test with coverage (CI-like)
dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"

# Integration tests (requires Docker)
dotnet run -c Release --project testbed

# Verify API baseline (run after any interface changes)
dotnet run --project tools/interface-api-check/InterfaceApiCheck.csproj -c Release -- \
  --generate \
  --baseline pengdows.crud.abstractions/ApiBaseline/interfaces.txt \
  --assembly pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll

# Verify no vendor directories committed
dotnet run --project tools/verify-novendor

# Benchmarks
dotnet run --project benchmarks/CrudBenchmarks -- --filter '*MyBenchmark*'

# Helper scripts
./build-packages.sh
./run-unit-tests.sh
./run-integration-tests.sh
```

- All unit tests and integration tests must pass with NO skipped tests.
- The entire unit-test suite finishes in under 30 seconds; investigate immediately if it approaches 3 minutes.

## Commit & Pull Request Guidelines

- Commits: short, imperative; optional prefixes `feat:`, `fix:`, `refactor:`, `chore:`.
- PRs: clear description, rationale, scope; link issues; list behavioral/provider impacts; include tests.
- Before review: ensure `dotnet build` and `dotnet test` pass locally.

## Related Projects

- **`pengdows.poco.mint`:** A code generation tool that inspects a database schema and generates C# POCOs with the correct `[Table]`, `[Column]`, `[Id]`, and `[PrimaryKey]` attributes for use with `pengdows.crud`.
- **`pengdows.crud.fakeDb`:** A powerful, standalone NuGet package that provides a fake ADO.NET provider. It is essential for writing fast, isolated unit tests for any data access logic based on ADO.NET interfaces, including code that uses `pengdows.crud` or Dapper.

## AI Agent Files

This repository contains guidance files for multiple AI coding assistants:
- `CLAUDE.md` — Claude Code
- `AGENTS.md` — OpenAI Codex / Agents
- `GEMINI.md` — Google Gemini (this file)
- `skills/claude/` — Claude Code skills (slash commands)
- `skills/codex/` — Codex agent references

All three guidance files share the same core technical information.
