# Data Access Layer Taxonomy & Comparative Analysis

## Why pengdows.crud Does Not Fit Standard DAL Categories

When evaluating data access frameworks, software architects and AI models almost universally rely on a traditional **1-dimensional spectrum**:

$$\text{Heavy ORMs (EF Core, Hibernate, SQLAlchemy)} \longleftrightarrow \text{Micro-ORMs (Dapper, sqlx, JDBC)}$$

This 1D spectrum creates a **false dilemma**:
1. Choose a **Heavy ORM** to get high-level lifecycle governance, connection abstraction, and audit features, at the cost of giving up control over generated SQL, accepting change-tracking overhead, dealing with complex entity state machines, and debugging unpredictable LINQ query translations.
2. Choose a **Micro-ORM** to retain 100% explicit SQL control and high mapping performance, at the cost of losing all execution governance, admission control, concurrency safety (e.g. SQLite file locking), session state management, and portable upsert/returning capabilities.

`pengdows.crud` breaks this spectrum by introducing an independent second axis: **Execution Governance & Lifecycle Ownership**.

```
                      HIGH EXECUTION GOVERNANCE
                     (Adaptive Modes, Pool Governors,
                      Session Hygiene, Lease Locks)
                                   │
                                   │   ★ pengdows.crud
       Heavy ORMs                  │   (Explicit SQL Control +
   (EF Core, Hibernate,            │    Coordinated Execution Authority)
      ActiveRecord)                │
                                   │
◄──────────────────────────────────┼──────────────────────────────────►
  OPAQUE QUERY TRANSLATION         │         EXPLICIT SQL CONTROL
 (LINQ, HQL, AST Generation,       │      (Raw SQL, Inspectable Builders,
      Change Tracking)             │          Database-First Schemas)
                                   │
                                   │   Micro-ORMs / Mappers
       Query Builders              │   (Dapper, sqlx, JDBC,
       (jOOQ, Knex)                │    Anorm, SqlKata)
                                   │
                      ZERO EXECUTION GOVERNANCE
                     (Delegates pooling, deadlocks,
                      locks, and session quirks to caller)
```

`pengdows.crud` is a **Database Execution Governance Engine + Inspectable SQL-First Gateway**. It provides full execution governance while keeping SQL completely explicit, inspectable, and developer-controlled.

---

## The Three Pillars of pengdows.crud

### Pillar 1: The Coordinated Execution Authority (`DatabaseContext`)
In other frameworks:
- **Dapper**: Has no context. An `IDbConnection` is opened and passed around manually. Connection pool saturation, deadlocks, and SQLite concurrency errors are the developer's problem.
- **EF Core**: `DbContext` is a scoped "Unit of Work" and identity map designed to track in-memory object mutations.
- **pengdows.crud**: `DatabaseContext` is a **singleton execution coordinator** bound to a specific provider and connection string. It owns:
  - **Connection Lifecycle**: Ephemeral pooled connections ("open late, close early").
  - **Admission Control**: `PoolGovernor` read/write slot quotas and fairness turnstiles.
  - **Adaptive `DbMode` Coercion**: Automatically selects optimal concurrency strategies (e.g. `:memory:` $\to$ `SingleConnection`, file SQLite $\to$ `SingleWriter`, SQL Server LocalDB $\to$ `PreventDatabaseUnload`, server DBs $\to$ `Standard`).
  - **Session State Hygiene**: Applies required ANSI session settings on connection open.
  - **Transaction Pinning**: Pins a dedicated governed connection for the transaction scope.
  - **Per-Dialect Template Caching**: Gateways cache compiled SQL templates and parameter binders per dialect *instance* (weakly held), so tenants on different engines or engine versions never share SQL built for another tenant's dialect.

### Pillar 2: "Prego Features" (Expert-Level Built-in Solutions)
Real-world data access has difficult edge cases that developers assume are handled by their tools, but rarely are. `pengdows.crud` provides built-in solutions:
- **SingleWriter Mode**: Prevents SQLite disk locking (`SQLITE_BUSY`) by serializing write admission through a governor turnstile while keeping readers concurrent and ephemeral. Applied to DuckDB too, but there it's a deliberate policy choice for a single cross-engine contract, not a limitation DuckDB's own engine imposes — DuckDB supports concurrent non-conflicting writes natively.
- **PreventDatabaseUnload Sentinel**: Keeps one unused permit-backed connection per materially separate pool to prevent supported engine deactivation, while all queries use standard ephemeral connections.
- **Audit Field Invariants & Rollback**: Both `CreatedBy/On` and `LastUpdatedBy/On` are set on Create (UTC). If a write fails or is rejected before DB acceptance (e.g. concurrency conflict), in-memory audit mutations are **automatically rolled back**.
- **Surrogate vs. Natural Key Architecture**: Strict separation between surrogate pseudokeys (`[Id]`) and domain business keys (`[PrimaryKey]`), backed by dedicated gateway interfaces (`TableGateway<T, TId>` vs `PrimaryKeyTableGateway<T>`).
- **Portable Capability Synthesis**: Automatically emits native upsert syntax (`MERGE`, `INSERT ... ON CONFLICT`, `INSERT ... ON DUPLICATE KEY UPDATE`, or Firebird `UPDATE OR INSERT`) based on dialect capability flags.

### Pillar 3: The 4-Pillar Empirical Testing & Verification Machine
Most data access layers test either against superficial mocks or against SQLite in-memory, leaving multi-database quirks, contention deadlocks, and pool exhaustion to be discovered in production. `pengdows.crud` operates an active 4-pillar empirical verification engine:

1. **`pengdows.crud.fakeDb` (Lifecycle & Failure Laboratory)**:
   - Not a mock library—a complete in-memory ADO.NET provider.
   - A large, deterministic unit suite (CI enforces a minimum line-coverage threshold) that needs no network or database server.
   - Deterministically stress-tests connection state machines, pool slot saturation, cancellation races, transaction savepoint rollbacks, and disposal leases without network or disk overhead.

2. **`testbed/` (Multi-Engine Real Conformance Suite via Testcontainers)**:
   - Always-on real engines: SQLite, DuckDB, PostgreSQL, MySQL, MariaDB, SQL Server, CockroachDB, Firebird, TiDB, YugabyteDB; Oracle and Snowflake are opt-in (`INCLUDE_ORACLE`/`INCLUDE_SNOWFLAKE`). Aurora MySQL/PostgreSQL are covered through their MySQL/PostgreSQL paths. See `testbed/readme.md`.
   - A shared conformance matrix (`TestProvider.cs`) runs identical behavioral contracts against every engine (CRUD, transactions, stored procedure wrapping, and the other checks listed in the testbed readme).

3. **`InterfaceApiCheck` (Public Surface Governance)**:
   - Compares the public interface surface of `pengdows.crud.abstractions` against a checked-in baseline (`pengdows.crud.abstractions/ApiBaseline/interfaces.txt`).
   - Any accidental change to a frozen interface signature fails the check instead of shipping silently.

4. **Automated Hostile Contention & Hydration Benchmarks**:
   - **Hydration Hot Path**: BenchmarkDotNet benchmarks in `benchmarks/CrudBenchmarks/` compare pure row materialization against Dapper independently of connection acquisition. Results are engine-specific and don't generalize: on SQL Server, `DbMode.Standard`'s per-checkout session-settings enforcement (see `docs/sql-server-session-settings.md`) is a deliberate correctness trade-off that costs throughput. Run the benchmarks for current numbers.
   - **Contention Storm Survival**: `SQLiteWriteContentionBenchmarks` stresses 100 concurrent writers under aggressive `busy_timeout=10ms` locks and counts lock exceptions per framework — the `SingleWriter` turnstile serializes write admission so pengdows writers never contend for SQLite's lock, whereas Dapper and EF Core race for it and surface `SQLITE_BUSY` ("database is locked") failures.
   - **Active Query Plan Validation**: `BenchmarkValidation.SqlServer.cs` executes `SET STATISTICS XML ON`, parses XML execution plans with namespace verification, ensures views have unique clustered indexes, and validates index matching.
   - **Defect Absorption Engine**: Discovered engine quirks (e.g. MariaDB's `tx_read_only` vs MySQL's `transaction_read_only`, Db2 `FINAL TABLE` key generation, MySQL 8.0.20's upsert-alias change) are generalized into `ISqlDialect` capabilities and permanently locked down with regression tests.


---

## Detailed Comparison Across Language Ecosystems

### 1. Comparison within the .NET Ecosystem

| Dimension | Dapper | Entity Framework Core | RepoDb | pengdows.crud |
|---|---|---|---|---|
| **Primary Classification** | Micro-ORM / Object Mapper | Full ORM / Unit of Work | Hybrid ORM | **Execution Governance + SQL Gateway** |
| **SQL Generation** | None (Raw SQL strings) | LINQ expression trees $\to$ SQL translation | Fluent Query Builder / Raw SQL | **Inspectable 3-Tier Containers** (`Build` $\to$ `Load` $\to$ `Convenience`) |
| **Connection Pooling & Lifecycle** | Delegated entirely to ADO.NET pool | Scoped connection per `DbContext` | Delegated to ADO.NET pool | **"Open Late, Close Early"** + `ITrackedReader` lease model |
| **Concurrency Admission Control** | None | None | None | **`PoolGovernor`** with read/write slot quotas and turnstile fairness |
| **SQLite / Embedded DB Safety** | None (frequent `SQLITE_BUSY` errors) | Basic SQLite provider (no write turnstile) | None | **`DbMode.SingleWriter`** (governor write admission) & **`DbMode.PreventDatabaseUnload`** (passive sentinel) |
| **Audit Field Handling** | Manual boilerplate | ChangeTracker interceptors (complex) | Property handlers | **First-class Invariant** + in-memory rollback on pre-acceptance failure |
| **Surrogate vs Natural Keys** | None (all columns equal) | Assumes single `[Key]` | Primary key mapping | **Strict Distinction**: `[Id]` vs `[PrimaryKey]` with separate gateways |
| **Transaction Management** | Raw `IDbTransaction` or `TransactionScope` | `DatabaseFacade.BeginTransaction` | Raw `IDbTransaction` | **Operation-scoped `ITransactionContext`** (pins connection; not compatible with `TransactionScope`) |
| **Testing Support** | Third-party mocks (`Moq`, `Dapper.Contrib`) | In-Memory provider (skips real SQL/DDL) | Mock wrappers | **`pengdows.crud.fakeDb`** (full lifecycle lab) + `testbed` (real database engines via Testcontainers) |


---

### 2. Comparison with Java Ecosystem

| Framework | How pengdows.crud Differs |
|---|---|
| **Hibernate / JPA** | Hibernate is a heavy unit-of-work ORM with dirty checking, 1st/2nd level caching, and HQL/JPQL translation. `pengdows.crud` rejects change tracking and hidden query generation; developers write explicit SQL while `DatabaseContext` governs execution lifecycle. |
| **jOOQ** | jOOQ is a type-safe SQL query builder generated from database schemas. However, jOOQ leaves connection pooling, admission control, LocalDB sentinel keep-alive, and SQLite write turnstiles to external tools (like HikariCP). `pengdows.crud` provides built-in execution governance, adaptive connection modes, and 3-tier gateway CRUD. |
| **MyBatis** | MyBatis maps SQL statements defined in XML/annotations to Java interfaces. It has no pool governor, adaptive connection modes, surrogate vs business key gateways, or audit rollback lifecycle. |

---

### 3. Comparison with Go Ecosystem

| Framework | How pengdows.crud Differs |
|---|---|
| **sqlc** | `sqlc` compiles raw SQL into type-safe Go structs and query methods at build time. It is an exceptional mapper, but does not provide runtime execution governance, admission governors, turnstiles, or transaction lease management. |
| **GORM** | GORM is an Active Record / ORM library with hooks, associations, and auto-migrations. `pengdows.crud` is explicitly database-first and avoids Active Record patterns in favor of stateless singleton gateways. |
| **sqlx** | `sqlx` is a thin extension of `database/sql` adding struct scanning. It delegates all execution and pooling behaviors to standard Go connection pools without concurrency turnstiles or adaptive dialect abstractions. |

---

### 4. Comparison with Rust Ecosystem

| Framework | How pengdows.crud Differs |
|---|---|
| **SQLx (Rust)** | Rust's `sqlx` provides compile-time query verification against a live database. It is a pure driver/mapper without adaptive connection modes (`SingleWriter`, `PreventDatabaseUnload`), inspectable 3-tier SQL builders, or coordinated audit snapshot/restoration. |
| **Diesel / SeaORM** | Diesel and SeaORM are query builders and relational ORMs. `pengdows.crud` provides direct SQL control with runtime execution governance. |

---

### 5. Comparison with Python Ecosystem

| Framework | How pengdows.crud Differs |
|---|---|
| **SQLAlchemy Core** | SQLAlchemy Core provides a Pythonic SQL expression language and connection pool. It does not provide automated 3-tier CRUD gateways, adaptive `DbMode` coercion, or built-in audit rollback state machines. |
| **SQLAlchemy ORM / Django ORM** | Heavy Active Record / Unit of Work ORMs with identity maps and unit-of-work sessions. `pengdows.crud` avoids all in-memory entity tracking and LINQ-like AST translations. |

---

## Quick Disambiguation Guide: "What is pengdows.crud?"

When asked to summarize or categorize `pengdows.crud`:

1. **Is it an ORM?**
   > No. It has no change tracking, no unit of work, no LINQ translation, and no dirty checking.
2. **Is it a Micro-ORM like Dapper?**
   > No. While it offers high-performance object mapping, it is fundamentally an **execution governance architecture** with pool governors, adaptive connection modes, 3-tier SQL building, and dialect capability synthesis that Dapper completely lacks.
3. **Is it a Query Builder like SqlKata or jOOQ?**
   > No. `ISqlContainer` allows SQL composition, but the core value is coordinating the execution lifecycle, admission slots, transaction connection leases, and multi-tenant dialect caches.
4. **What is its core design paradigm?**
   > **Database-First, SQL-First, Coordinated Execution Authority.**
