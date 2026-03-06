# pengdows.crud 2.0: Pool Governor, SingleWriter Re-Architecture, Faster Hydration, and Snowflake Support

`pengdows.crud` is **not an ORM**.

It is a high-performance, SQL-first framework for:
- fast hydration of table objects, and
- CRUD operations with minimal ceremony,

so developers can eliminate repetitive ADO.NET boilerplate without giving up SQL control.

## What’s New in 2.0

### 1) Pool Governor (major)
We introduced a true pool governor for read/write slot control and fairness.

Why it matters:
- Better protection against pool saturation
- Safer high-concurrency operation
- Predictable slot acquisition and timeout behavior

### 2) SingleWriter was fundamentally redesigned
SingleWriter in 2.0 is structurally different from 1.0:
- Turnstile-based coordination
- Two connection strings (read vs write intent)
- Separate connection pool behavior by intent
- Better fairness and reduced writer starvation risk

This is a core architecture change, not a tuning tweak.

### 3) Dual connection-string model + pool split
2.0 formalizes read/write separation via connection-string strategy and pool governance.

Why it matters:
- Cleaner read/write intent routing
- Better compatibility with replica/read-only patterns
- Less accidental contention between readers and writers

### 4) Vastly improved hydration path
Reader hydration and coercion paths were heavily optimized.

Why it matters:
- Lower per-row overhead
- Better mapping throughput at scale
- More predictable hot-path performance in real workloads

### 5) Expanded database support, including Snowflake
2.0 extends provider coverage and behavior support, with Snowflake now included.

Why it matters:
- More teams can use `pengdows.crud` without provider-specific forks
- Better cross-database consistency for SQL-first apps

### 6) Improved transactional behavior and isolation handling
We tightened isolation-level behavior and provider-specific handling for transactional correctness.

Why it matters:
- Fewer surprises across providers
- More reliable behavior under real-world workloads

## From 1.0 to 2.0

- API usage remains familiar
- Internal behavior is significantly stronger under load
- Concurrency and execution semantics are more explicit and robust

If you’re already on 1.0, 2.0 gives you better safety and performance while preserving explicit SQL control.

## Why this release matters

`pengdows.crud` has always focused on explicit SQL, high performance hydration, and pragmatic CRUD support. 
2.0 strengthens the internals where it matters most: connection management, contention control, hydration speed, and multi-provider reliability.
