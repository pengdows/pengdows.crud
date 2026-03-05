# pengdows.crud: The Database-Respecting SQL Engine
## Strategy & Narrative for the Oracle Developers Conference

### 1. The Core Philosophy: "Expert in a Box"
Most data access tools treat the database as a "dumb bit-bucket." They optimize for developer convenience at the expense of the database instance. `pengdows.crud` is a **SQL-first, Database-First** engine built on the principle of **Database Respect**. It provides automated, high-performance "plumbing" while enforcing the architectural rigor that mission-critical Oracle environments demand.

---

### 2. The "Hydration Tax" & Compiled Efficiency
*   **The Problem:** 80% of data access code is boring, brittle "Hydration" (mapping `IDataReader` to objects). Manual code is fragile; one schema change can break the app at 3:00 AM.
*   **The Solution:** `pengdows.crud` kills the boilerplate. It uses **Expression Trees (IL Generation)** to "bake" high-speed hydrators the first time a type is seen.
*   **Performance:** It is as fast as hand-written ADO.NET because it *is* hand-written IL at runtime—zero reflection on the hot path.
*   **Schema Sentinel:** Using `pengdows.poco.mint`, the Oracle Data Dictionary remains the **Source of Truth**. Your C# POCOs are generated from the actual schema, ensuring compile-time safety for your data tier.

---

### 3. Pillar: Instance Protection & Resource Integrity
*   **The "Zero-Leak" Invariant:** Connection leaks are a denial-of-service attack on an Oracle instance. `pengdows.crud` is built on a strict **`IAsyncDisposable`** lifecycle. The engine manages the "Open Late, Close Early" pattern internally; the code **cannot** leak because the framework owns the cleanup logic.
*   **The Resource Governor:** Instead of letting a "thundering herd" of app requests hammer the database, `pengdows.crud` uses an internal **Governor/Turnstile**. It converts **Database Pressure into Application Backpressure**. 
*   **The Quote:** *"I’d rather my app wait for a turnstile than my database crash under a thundering herd."*

---

### 4. Pillar: Perfect, Index-Aligned SQL
*   **Deterministic SQL:** Oracle’s Optimizer thrives on consistency. The `TableGateway` ensures every identifier is wrapped, every table aliased, and every piece of data parameterized. This maximizes **Cursor Sharing** and plan stability in the Library Cache.
*   **The "Two-Index" Rule:** Operations only touch data via the **Surrogate [Id]** or the **Business [PrimaryKey]**. 
*   **Index Awareness:** The engine respects the **Column Order** of your composite primary keys. It builds the `WHERE` clause to match your physical index definition in Oracle, ensuring a **Guaranteed Index Seek** every time.

---

### 5. Pillar: Observable Workload Isolation
*   **Dual-Pool Architecture:** The engine maintains two distinct, searchable connection pools:
    *   **Read-Only Pool:** Appended with `{application-name}-ro`.
    *   **Write Pool:** Appended with `{application-name}-rw`.
*   **DBA Visibility:** A DBA can instantly filter `V$SESSION` or `V$SQL` by `-ro` or `-rw`. They can trace, log, and optimize these workloads independently.
*   **High-Availability:** Point the `-ro` pool at an **Oracle Active Data Guard** standby and the `-rw` pool at the primary. The engine handles the routing; the developer just writes code.

---

### 6. Pillar: Hard-Enforced Security
*   **Credential Isolation:** Oracle lacks a reliable `ReadOnly=True` connection string flag. `pengdows.crud` solves this via its dual-pool design.
*   **Physical Enforcement:** Use a low-privilege Oracle user (SELECT only) for the `-ro` pool and a transactional user for the `-rw` pool. If a developer attempts an `UPDATE` in a read-path, the **Database itself** blocks it with an `ORA-01031`. It’s not "Read-Only" by convention; it’s **Read-Only by Physical Enforcement**.

---

### 7. Pillar: Deep Expertise & Multi-DB Leverage
*   **Connection Mode Precision:** While we use `Standard` mode for Oracle to respect its high-concurrency architecture, the engine includes specialized modes like `SingleWriter` (for SQLite/DuckDB) and `SingleConnection` (for in-memory testing). This proves architectural depth over "one-size-fits-all" mapping.
*   **The "Icing":** The same "DBA-First" logic works against **15+ supported databases** (Snowflake, Postgres, SQL Server, CockroachDB, etc.). You aren't just choosing a library; you're choosing an **Enterprise Data Access Standard**.

---

### 8. Radical Accountability: The Telemetry Suite
*   **The Flight Recorder:** Every `DatabaseContext` tracks real-time **Database Metrics**.
*   **Evidence-Based Debugging:** If a developer does something reckless (e.g., a static transaction), the metrics will record it. `TransactionsActive`, `ConnectionsCurrent`, and `LongLivedConnections` will provide the "smoking gun" needed to find the leak before it impacts production.

---

### **The "Mic Drop" Closing Statement**
> "I built `pengdows.crud` because I got sick of writing brittle hydration code for every 5 lines of SQL, and I wanted a system where my database schema **enforces** my C# models, not the other way around. 
> 
> It’s not just a mapper; it’s a **Resource Governor** that treats your Oracle instance with the respect it deserves. It gives the developer speed, it gives the DBA observability, and it gives the instance stability. It is the first framework your DBA will actually **thank you** for using."
