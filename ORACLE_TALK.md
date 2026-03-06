# pengdows.crud: The Database-Respecting SQL Engine
## Strategy & Narrative for the Oracle Developers Conference

### 1. The Core Philosophy: "Expert in a Box"
Most data access tools treat the database as a "dumb bit-bucket." They optimize for developer convenience at the expense of the database instance. `pengdows.crud` is a **SQL-first, Database-First** engine built on the principle of **Database Respect**. It provides automated, high-performance "plumbing" while enforcing the architectural rigor that mission-critical Oracle environments demand.

---

### 2. The "Hydration Tax" & Compiled Efficiency
*   **The Problem:** 80% of data access code is boring, brittle "Hydration" (mapping `IDataReader` to objects). Manual code is fragile; one schema change can break the app at 3:00 AM.
*   **The Solution:** `pengdows.crud` kills the boilerplate. It uses **Expression Trees compiled to IL delegates** to "bake" high-speed hydrators the first time a type is seen.
*   **Performance:** It is as fast as hand-written ADO.NET because it *is* hand-written IL at runtime—zero reflection on the hot path.
*   **Schema Sentinel:** Using `pengdows.poco.mint`, the Oracle Data Dictionary remains the **Source of Truth**. Your C# POCOs are generated from the actual schema, ensuring compile-time safety for your data tier.

---

### 3. Pillar: Instance Protection & Resource Integrity
*   **The "Zero-Leak" Discipline:** Connection leaks are a denial-of-service attack on an Oracle instance. `pengdows.crud` is built on a strict **`IAsyncDisposable`** lifecycle and the engine manages the "Open Late, Close Early" pattern internally to reduce leak risk and enforce prompt cleanup.
*   **The Resource Governor:** Instead of letting a "thundering herd" of app requests hammer the database, `pengdows.crud` uses internal read/write **Pool Governors** to convert **Database Pressure into Application Backpressure**.
*   **Turnstile Context:** For embedded single-writer engines (SQLite/DuckDB), it also supports turnstile-style fairness. Not Oracle-specific, but it demonstrates engine-level connection-mode depth.
*   **The Quote:** *"I’d rather my app wait for a turnstile than my database crash under a thundering herd."*

---

### 4. Pillar: Perfect, Index-Aligned SQL
*   **Deterministic SQL:** Oracle’s Optimizer thrives on consistency. The `TableGateway` ensures every identifier is wrapped, every table aliased, and every piece of data parameterized. This maximizes **Cursor Sharing** and plan stability in the Library Cache.
*   **The "Two-Index" Rule:** Operations only touch data via the **Surrogate [Id]** or the **Business [PrimaryKey]**. 
*   **Index Awareness:** The engine respects the **Column Order** of your composite primary keys. It builds the `WHERE` clause to match your physical index definition in Oracle, maximizing the probability of an **Index Seek** on every access — the optimizer sees exactly the column order it expects.
*   **Native Oracle SQL:** Upserts use Oracle's native `MERGE` statement — not a read-then-write simulation. The optimizer can plan it as a single atomic statement.

---

### 5. Pillar: Observable Workload Isolation
*   **Dual-Pool Architecture:** The engine maintains two structurally separate connection pools — one for reads, one for writes. Each has its own connection string, its own pool governor, and its own connection lifecycle.
*   **DBA Visibility:** For databases that support an `Application Name` connection-string parameter (SQL Server, PostgreSQL, Snowflake), the engine automatically appends `-ro` and `-rw` suffixes so a DBA can filter by workload in session views. For Oracle specifically, workload isolation is enforced through **separate Oracle users** — the `-ro` pool connects as a SELECT-only schema user; the `-rw` pool as a transactional user. Oracle's `V$SESSION.SCHEMANAME` becomes your visibility column.
*   **High-Availability:** Point the read pool at an **Oracle Active Data Guard** standby and the write pool at the primary. The engine handles the routing; the developer just writes code.

---

### 6. Pillar: Hard-Enforced Security
*   **Credential Isolation:** Oracle lacks a reliable `ReadOnly=True` connection string flag. `pengdows.crud` solves this via its dual-pool design.
*   **Physical Enforcement:** Use a low-privilege Oracle user (SELECT only) for the `-ro` pool and a transactional user for the `-rw` pool. If a developer attempts an `UPDATE` in a read-path, the **Database itself** blocks it with an `ORA-01031`. It’s not "Read-Only" by convention; it’s **Read-Only by Physical Enforcement**.

---

### 7. Pillar: Deep Expertise & Multi-DB Leverage
*   **Connection Mode Precision:** While we use `Standard` mode for Oracle to respect its high-concurrency architecture, the engine includes specialized modes like `SingleWriter` (for SQLite/DuckDB) and `SingleConnection` (for in-memory testing). This proves architectural depth over "one-size-fits-all" mapping.
*   **The "Icing":** The same "DBA-First" logic works against **14 supported databases** (Snowflake, Postgres, SQL Server, CockroachDB, YugabyteDB, Firebird, DuckDB, and more). You aren't just choosing a library; you're choosing an **Enterprise Data Access Standard**.

---

### 8. Radical Accountability: The Telemetry Suite
*   **The Flight Recorder:** Every `DatabaseContext` tracks real-time **Database Metrics**.
*   **Evidence-Based Debugging:** If a developer does something reckless (e.g., a static transaction), the metrics will record it. `TransactionsActive`, `ConnectionsCurrent`, and `LongLivedConnections` will provide the "smoking gun" needed to find the leak before it impacts production.

---

### **The "Mic Drop" Closing Statement**
> "I built `pengdows.crud` because I got sick of writing brittle hydration code for every 5 lines of SQL, and I wanted a system where my database schema **enforces** my C# models, not the other way around. 
> 
> It’s not just a mapper; it’s a **Resource Governor** that treats your Oracle instance with the respect it deserves. It gives the developer speed, it gives the DBA observability, and it gives the instance stability. It is the first framework your DBA will actually **thank you** for using."

---

## Appendix: The Evolution of pengdows.crud

### How frustration with ADO.NET and ORMs turned into a deterministic SQL runtime

pengdows.crud didn’t begin as a framework. It began as irritation. I was tired of writing raw ADO.NET. Tired of arguing about connection reuse. Tired of production issues caused by abstraction layers that hid too much. And tired of watching databases treated as runtime side-effects instead of first-class systems.

This is the story of how pengdows.crud evolved — not from theory, but from repeated friction in real systems.

---

#### Phase 1 — The Connection Argument
At one client, I was told: *“Reuse the open connection as much as possible.”* The assumption was that `Open()` is expensive and long-lived connections are efficient. I disagreed. I wrote a test that executed hundreds of thousands of Open/Execute/Close cycles. The overhead was negligible compared to the risk of stale connections, leaked state, and unpredictable behavior.

That experiment created the first principle: **Connections should be short-lived and deterministic.** But writing open/execute/close boilerplate everywhere is noise. So I built a small object to hold SQL in a `StringBuilder`, manage parameters, and wrap the lifecycle. That object became the ancestor of what is now `SqlContainer`.

#### Phase 2 — Context Ownership
As the system grew, a deeper issue emerged: If SQL is just a string builder, how does it know which database, provider, tenant, or transaction to use? The answer: **SQL belongs to the context that created it.** This led to `DatabaseContext` and `TransactionContext`. From that point forward, SQL was never free-floating; it was always bound to an execution context.

#### Phase 3 — Dialect Reality
Testing across providers exposed the next problem: different identifier quoting rules, parameter markers, and session-level quirks. The lesson: **Correct SQL is dialect-aware.** Every identifier is quoted. Every column is explicitly listed. Every parameter is provider-correct. No `SELECT *`. No implicit schema assumptions.

#### Phase 4 — Mapping Without Magic
Hand-writing hydration code is tedious. Reflection solved that via attributes like `[Table]`, `[Column]`, `[Id]`, and `[Version]`. But there was one deliberate omission: **No LINQ provider.** Years earlier, I had broken production by removing a `1 == 1` predicate from a LINQ query that changed the SQL in subtle ways. From that point on: **SQL shape must be deterministic.** Refactoring C# should not silently rewrite your SQL.

#### Phase 5 — DB-First Discipline
I was repeatedly brought into projects to rewrite code-first systems collapsing under migrations or change tracking. The database was not treated as a first-class design element. So the philosophy became explicit: **The schema is the source of truth.** Business identity (`[PrimaryKey]`) and surrogate identity (`[Id]`) are separate concerns.

#### Phase 6 — Connection Modes
Different engines require different lifecycle strategies. Connection handling became formalized into modes: `Standard` (per operation), `KeepAlive` (sentinel), `SingleWriter` (serialized writes), and `SingleConnection` (testing). These modes express operational policy — not feature differences.

#### Phase 7 — Pool Governance
Connection pools are opaque. You can saturate them and collapse your app without warning. So I built a governor. `PoolGovernor` caps concurrent operations, applies backpressure, and fails deterministically on timeout. This was not about speed; it was about stability under load.

#### Phase 8 — Read/Write Separation
Modern systems use read replicas and separate permissions. Configuration evolved to support separate read and write connection strings, separate governors, and session-level read-only enforcement. Topology became explicit.

#### Phase 9 — Audit Injection
Audit fields like `CreatedBy` and `LastUpdatedOn` are boilerplate. Instead of scattering logic everywhere, it became injectable via `IAuditValueResolver`. This eliminated repetitive code across services.

#### Phase 10 — Multi-Tenant Formalization
Many systems equate Tenant to Database. I formalized this into `TenantContextRegistry`. Each tenant receives its own `DatabaseContext`, own governance, and own configuration. Isolation by design.

#### Phase 11 — TableGateway
Originally called `EntityHelper`, it was renamed `TableGateway` to reflect its purpose: **A deterministic SQL toolkit for a single table.** It generates SQL, applies audit logic, enforces optimistic concurrency, and honors dialect rules. It does not track object graphs or infer joins.

---

### The Core Principle
Every feature traces back to a single invariant: **SQL belongs to the execution context that created it.** That invariant enables tenant isolation, transaction scoping, retry compatibility, dialect normalization, pool governance, and read/write routing. 

pengdows.crud was not designed top-down. It evolved from production issues, architectural debates, and operational failures. Each feature exists because something broke once. The goal: **Make database interaction explicit, deterministic, and disciplined.**

