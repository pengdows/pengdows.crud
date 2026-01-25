# Repository Guidelines

## Project Structure & Module Organization
- Source: `pengdows.crud/` (core), `pengdows.crud.abstractions/` (interfaces), `pengdows.crud.fakeDb/` (in-memory provider), `testbed/` (integration suite via Testcontainers).
- Tests: `pengdows.crud.Tests/` (xUnit). Coverage and TRX under `TestResults/`.
- Solution: `pengdows.crud.sln`. CI: `.github/workflows/deploy.yml`.
- Benchmarks: `benchmarks/CrudBenchmarks/` (BenchmarkDotNet suite; run before shipping perf-sensitive changes).

## Build, Test, and Development Commands
- Restore: `dotnet restore`
- Build: `dotnet build pengdows.crud.sln -c Release` (treats warnings as errors for libraries).
- Test (local): `dotnet test -c Release --results-directory TestResults --logger trx`
- Test with coverage (CI-like):
  `dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"`
- Pack (NuGet): `dotnet pack <project>.csproj -c Release`
- Integration suite (requires Docker): `dotnet run -c Release --project testbed`
- `testbed` is the integration testing app; treat it as part of the primary verification flow.
- Whenever work is completed, ensure all unit tests and integration tests pass with no skipped tests.
- If the intended functionality is unclear, consult the wiki (`pengdows.crud.wiki/`) or ask for clarification before proceeding.
- Tooling: utilities are in `tools/`; run `dotnet run --project tools/verify-novendor` to ensure no vendor directories are committed.

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
- Hide implementation details as `internal` by default. Nothing outside of `DatabaseContext` should expose a public constructor—objects should either be resolved through factory helpers, DI, or internal constructors (exceptions require documented rationale). This keeps the SDK surface stable and highlights the interface-first programming model.

## Interface-first Mandate
- Always code against the interface contract; implementation classes exist only to fulfill the abstractions defined in `pengdows.crud.abstractions`.
- Treat interfaces such as `ITableGateway`, `IDatabaseContext`, `ISqlContainer`, `ISqlDialect`, etc. as the primary SDK surface—new code should depend on those APIs rather than concrete helpers.
- Keep concrete types internal unless a public contract is required, and do not introduce public constructors except for `DatabaseContext` (all other instantiations should happen through factory helpers, DI, or internal factory methods).

## Interfaces & Extension Points
- `IDatabaseContext`: entry point. Create via DI or `new DatabaseContext(connStr, DbProviderFactory)`. Builds `ISqlContainer`, formats names/params, and controls connections/transactions.
- `ISqlContainer`: compose SQL safely and execute.
  Example: `var sc = ctx.CreateSqlContainer("SELECT 1"); var v = await sc.ExecuteScalarAsync<int>();`
- `IEntityHelper<TEntity, TRowID>`: SQL-first CRUD with inspectable containers.
  Example: `var sc = helper.BuildRetrieve(new[] { id }); var e = await helper.LoadSingleAsync(sc);`
- `ITransactionContext`: `using var tx = ctx.BeginTransaction(); ... tx.Commit();` Pass `tx` to helper methods when you want execution inside the transaction.
- `IAuditValueResolver`: implement to supply user/time; register in DI so audit fields populate consistently.
- Advanced: implement `ISqlDialect`/`IDbProviderLoader` to add/override provider behavior.

## CRITICAL: Pseudo Key (Row ID) vs Primary Key (Business Key)

**DO NOT CONFUSE THESE CONCEPTS.**

| Concept | Attribute | Columns | Purpose |
|---------|-----------|---------|---------|
| **Pseudo Key / Row ID** | `[Id]` | Always single | Surrogate identifier for EntityHelper operations, FKs, easy lookup |
| **Primary Key / Business Key** | `[PrimaryKey(n)]` | Can be composite | Natural key - why the row exists in business terms |

**Key Rules:**
1. `[Id]` and `[PrimaryKey]` are MUTUALLY EXCLUSIVE on a column - never both on the same property
2. EntityHelper REQUIRES `[Id]` for `CreateAsync`, `UpdateAsync`, `DeleteAsync(TRowID)`
3. `[Id(false)]` = DB-generated (autoincrement); `[Id]` or `[Id(true)]` = client-provided
4. `[PrimaryKey]` defines business uniqueness, enforced via UNIQUE constraint in DDL
5. Both can coexist on different columns: pseudo key for operations, business key for domain integrity
6. `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns; `DeleteAsync(TRowID)` uses `[Id]`

**Example:**
```csharp
[Table("order_items")]
public class OrderItem
{
    [Id(false)]           // Pseudo key - DB auto-generates
    [Column("id")] public long Id { get; set; }

    [PrimaryKey(1)]       // Business key part 1
    [Column("order_id")] public int OrderId { get; set; }

    [PrimaryKey(2)]       // Business key part 2
    [Column("product_id")] public int ProductId { get; set; }
}
```

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

## Id Attribute: Writable vs Non-Writable

| Attribute | Meaning | INSERT behavior |
|-----------|---------|-----------------|
| `[Id]` or `[Id(true)]` | Client provides value | Id column included in INSERT |
| `[Id(false)]` | DB generates value (autoincrement/identity) | Id column omitted from INSERT |

**SQL Server note:** Attempting to insert a value into an IDENTITY column throws an error unless `SET IDENTITY_INSERT ON`.

## Multi-Tenancy

pengdows.crud uses **context-per-tenant** (not query filtering):

- Each tenant gets a separate `DatabaseContext` (different connection string/database)
- Request resolves which context to use
- All operations use that context - no additional filtering required
- **No "WHERE tenant_id = X" injection** - tenants are physically separated

## ExecutionType (Read vs Write)

`ExecutionType` declares intent so the context can provide the appropriate connection:

| Type | Intent | Connection behavior |
|------|--------|---------------------|
| `ExecutionType.Read` | Read-only operation | May get ephemeral or shared connection |
| `ExecutionType.Write` | Modifying operation | Gets write-capable connection |

In `SingleWriter` mode, this determines whether you get the pinned write connection or an ephemeral read connection.

## TypeMapRegistry.Register<T>()

**Explicit registration is NOT required.** `GetTableInfo<T>()` uses `GetOrAdd` - auto-builds on first access.

```csharp
// These are equivalent:
typeMap.Register<MyEntity>();           // Explicit pre-registration
typeMap.GetTableInfo<MyEntity>();       // Auto-registers on first call
new EntityHelper<MyEntity, long>(ctx);  // Also triggers auto-registration
```

## Enum Storage

Enum storage format is determined by `DbType` in the `[Column]` attribute:

| DbType | Storage |
|--------|---------|
| `DbType.String` | Stored as enum name (string) |
| Numeric (`Int32`, etc.) | Stored as underlying numeric value |

**Throws** if DbType is neither string nor numeric.

```csharp
[Column("status", DbType.String)]    // Stored as "Active", "Inactive", etc.
public StatusEnum Status { get; set; }

[Column("priority", DbType.Int32)]   // Stored as 0, 1, 2, etc.
public PriorityEnum Priority { get; set; }
```

## RetrieveOneAsync(TEntity) Requirements

`RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns to find the row.

**If no `[PrimaryKey]` defined:** Throws `"No primary keys found for type {TypeName}"`

Use `RetrieveOneAsync(TRowID id)` for lookup by pseudo key instead.

## CRITICAL: Audit Field Behavior

**BOTH CreatedBy/On AND LastUpdatedBy/On are set on CREATE.**

This is intentional design - it allows "last modified" queries without checking if the entity was ever updated.

| Operation | CreatedBy | CreatedOn | LastUpdatedBy | LastUpdatedOn |
|-----------|-----------|-----------|---------------|---------------|
| **Create** | SET | SET | SET | SET |
| **Update** | unchanged | unchanged | SET | SET |

**Requirements:**
- If entity has `[CreatedBy]` or `[LastUpdatedBy]`, you MUST provide `IAuditValueResolver`
- Without resolver + user audit fields = `InvalidOperationException` at runtime
- Time-only audit fields (`[CreatedOn]`, `[LastUpdatedOn]`) work without resolver (uses `DateTime.UtcNow`)

## CRITICAL: Test-Driven Development (TDD) - MANDATORY

**ALL CODE MUST BE WRITTEN USING TDD. THIS IS NON-NEGOTIABLE.**

### TDD Workflow (Follow This Exactly)
1. **WRITE THE TEST FIRST** - Before ANY implementation code
2. **RUN THE TEST** - Verify it fails (red)
3. **WRITE MINIMAL IMPLEMENTATION** - Just enough to make the test pass (green)
4. **REFACTOR** - Improve code while keeping tests green
5. **REPEAT** - For every feature, bug fix, or change

### TDD Rules
- **NEVER** write implementation code before tests
- **NEVER** skip writing tests for "simple" changes
- **NEVER** commit code without corresponding tests
- Tests define the expected behavior - write them to lock in desired outcomes
- If you're unsure what to implement, the test will tell you

### Testing Infrastructure
- Framework: xUnit; mocks: Moq. Name files `*Tests.cs` and mirror source namespaces.
- Prefer `pengdows.crud.fakeDb` for unit tests; avoid real DBs. Use `testbed/` for integration via Testcontainers.
- Coverage artifacts live in `TestResults/`; CI publishes Cobertura from `TestResults/**/coverage.cobertura.xml`.
- The entire unit-test suite currently finishes in under 30 seconds; if a run approaches three minutes, terminate it and investigate for locking/hanging issues immediately.
- CI enforces minimum **83% coverage**; target **90%** for new work.
- Expand `fakeDb` when tests need behaviors it lacks - don't bypass its limitations.

## Commit & Pull Request Guidelines
- Commits: short, imperative; optional prefixes `feat:`, `fix:`, `refactor:`, `chore:`.
- PRs: clear description, rationale, scope; link issues; list behavioral/provider impacts; include tests.
- Before review: ensure `dotnet build` and `dotnet test` pass locally.

## Security & Configuration Tips
- Never commit secrets or real connection strings; use environment variables and user-secrets. Strong-name via `SNK_PATH` (do not commit keys).
- Do not hardcode identifier quoting. Use `WrapObjectName(...)` and `CompositeIdentifierSeparator` (e.g., ``var full = ctx.WrapObjectName("schema") + ctx.CompositeIdentifierSeparator + ctx.WrapObjectName("table");``).
- Always parameterize values (`AddParameterWithValue`, `CreateDbParameter`); avoid string interpolation for SQL.

## Additional Requirements
- All unit and integration tests (including `testbed` scenarios) must pass with NO skipped tests.
- Use `pengdows.crud.IntegrationTests` for database-specific behaviors; `testbed` for multi-provider verification.
- If `fakeDb` lacks needed mocking capabilities, ADD them to `fakeDb` - don't invent new mocking layers.
- When functionality is unclear, consult the wiki (`pengdows.crud.wiki/`) or ASK before proceeding.
