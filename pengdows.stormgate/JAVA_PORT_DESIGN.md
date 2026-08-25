# StormGate for Java (Spring / HikariCP / PostgreSQL) — design notes

Status: **design discussion only, nothing implemented yet.** Captured 2026-08-21 so this can be
picked back up later without re-deriving it.

## Motivating incident

A Spring/Postgres app consumes SQS messages (CSV payloads) and inserts them into a table with 5
indexes. A one-time catch-up replay of 11 million messages was taking days/weeks. The original
diagnosis was "slow inserts." Increasing the HikariCP connection pool size helped somewhat but did
not fix it.

Working conclusions from that discussion (see conversation history for full reasoning):

- **5 indexes on a write-heavy log table is a real cost** — every INSERT maintains all 5. For a
  one-time bulk catch-up specifically, the highest-leverage fix is unrelated to connection
  pooling: drop/disable the indexes, bulk-load via `COPY` (not row-by-row INSERT), rebuild indexes
  once after. This is a data/schema problem, not a concurrency-control problem, and should be
  fixed regardless of anything below.
- **"Slow inserts" as a diagnosis is incomplete** — without separately timing (a) time waiting for
  a pooled connection, (b) time the actual SQL statement takes to execute, and (c) application code
  time (CSV parsing, object construction) before/after the DB call, there's no way to know which of
  the three is actually dominant. This three-way split is the direct analogue of what
  `pengdows.crud`'s metrics system (command duration avg/P95/P99, reader lifecycle timing) provides
  natively in .NET — Java/Spring has no single tool that gives you all three out of the box.
- **Why "increasing the pool size helped somewhat but not enough" is meaningful, confirmed from
  HikariCP's actual source** (`brettwooldridge/HikariCP`, files fetched and read directly, not from
  memory):
  - `HikariPool.java:112-113`: `addConnectionExecutor` (the thread pool that actually establishes
    new physical connections) is sized to **`maximumPoolSize`** — the same number as pool capacity.
  - `ConcurrentBag.java:161-187` (`borrow()`): every caller that can't get an idle connection
    increments a waiter count and fires `listener.addBagItem(waiting)`.
  - `HikariPool.java:340-345` (`addBagItem`): each such event submits a new connection-creation task
    to `addConnectionExecutor`, bounded only by "don't queue more creation tasks than there are
    waiters" — not by any separate throttle on concurrent connection establishment.
  - **Conclusion:** `maximumPoolSize` is simultaneously HikariCP's capacity ceiling *and* its
    concurrent-new-connection-establishment ceiling — they are the same number by construction.
    Raising it to relieve pool contention during the SQS catch-up burst also raised how many brand
    new physical connections (full TCP handshake + auth + Postgres backend-process fork) could be
    opened against Postgres *simultaneously* during the ramp-up. That's a second plausible
    contributor to "helped somewhat, still not fast," independent of the index cost.
- **StormGate's actual differentiator, confirmed by contrast:** it never conflates "how many
  connections should exist for capacity" with "how much concurrent DB work should be admitted right
  now" — `maxConcurrentOpens` in `pengdows.stormgate/StormGate.cs` has no relationship to whatever
  `Max Pool Size` the underlying ADO.NET provider pool is configured with. Those are two
  independently tunable numbers. HikariCP has one number for both.

## Recommended approach: don't rebuild StormGate from scratch in Java

**Use Resilience4j's `Bulkhead` (specifically `SemaphoreBulkhead`)** — it's already exactly this
mechanism: a semaphore-gated concurrency limiter with a max-wait timeout. Mature, Apache 2.0 open
source, first-class Spring Boot integration (`resilience4j-spring-boot3`), Micrometer metrics
built in. No bespoke library needed; the work is deciding *where* to put the gate and wiring it up.

### Two placement options, with a real tradeoff

1. **Wrap the `DataSource`** — decorate `getConnection()` so the semaphore is acquired *before*
   Hikari ever sees the request; release it when the returned `Connection` closes.
   - `java.sql.Connection` already extends `AutoCloseable` (since JDBC 4.1) — no separate wrapper
     interface needed the way `PermitConnection : DbConnection` was needed in .NET. Just intercept
     the existing `close()`.
   - JDBC has **no equivalent of ADO.NET's `CommandBehavior.CloseConnection`** — a `ResultSet.close()`
     never auto-closes its parent `Connection` in JDBC. So the specific bug fixed in
     `pengdows.stormgate` (permit leaking because a reader closed the real inner connection out from
     under the wrapper) has no Java analogue — the JDBC port is structurally simpler and doesn't
     need the `StateChange`-subscription workaround `PermitConnection` now has.
   - Still need the same idempotency guard as `ReleasePermitOnce()` — `close()` can legitimately be
     called more than once (defensive code, or explicit close + try-with-resources both closing).
     Use `AtomicBoolean.compareAndSet(false, true)` around the actual permit release, mirroring
     `Interlocked.Exchange(ref _released, 1)`.
   - Composes with everything that consumes a `DataSource`: `JdbcTemplate`, JPA/Hibernate, Spring
     Batch's `JdbcCursorItemReader`, with one hook point.
2. **`@Bulkhead` on the service/repository method** (e.g. the SQS message handler doing
   parse-CSV-then-insert) — gates the whole unit of work, not just connection checkout. No
   `AutoCloseable`/`Connection` wrapping at all; Resilience4j acquires/releases the permit around
   the decorated method call automatically.
   - **Leaning toward this option for the SQS catch-up case specifically** — what actually needs
     throttling operationally is "how many messages are being processed at once," and connection
     admission is a downstream consequence of that, not the thing to gate directly.

### Why this also addresses the HikariCP fan-out finding above

If the gate admits at most N concurrent callers before they ever reach `getConnection()`, at most N
threads can ever be waiting on Hikari's `ConcurrentBag` at once — which caps how many
`addConnectionExecutor` tasks get submitted concurrently, regardless of `maximumPoolSize`. Size the
pool for steady-state capacity; size the gate for how much concurrent work to admit right now. Two
independently tunable numbers, which is exactly what HikariCP doesn't give you alone. The gate limit
should be set **below** `maximumPoolSize`, and ideally adjustable at runtime (Resilience4j supports
this) so it can be turned down live during an incident without touching pool config or restarting.

### Diagnostics — closing the "is it network, code, or DB" gap

Combine three independently-sourced timers to get the three-way breakdown discussed above (the
Java-world equivalent of `pengdows.crud`'s built-in metrics):

1. **Admission-wait time** — Resilience4j `Bulkhead` already publishes Micrometer metrics for
   permit-wait time and available/max permits.
2. **Pool-wait time** — HikariCP's own Micrometer integration (`hikaricp.connections.acquire`,
   `hikaricp.connections.pending`, etc.).
3. **Statement execution time** — not provided by Hikari/JDBC natively; needs a JDBC-level
   interceptor (`datasource-proxy` or `p6spy`) to time actual query execution specifically.

With all three wired to the same metrics backend, one glance tells you whether a slow path is
admission contention, pool starvation, or genuinely slow Postgres execution — instead of "slow
inserts" as an undifferentiated guess.

## Open questions / next steps (not yet decided)

- Confirm whether option (1) or (2) — or both, at different layers — is the right shape once there's
  a concrete app to try it against.
- If option (1) is chosen: decide whether to hand-write the `Connection` decorator or generate it via
  `java.lang.reflect.Proxy` (JDBC's `Connection` interface has ~60 methods; a dynamic proxy avoids
  writing all of them by hand at the cost of reflection overhead per call — measure before deciding).
- Decide whether a shared gate across multiple `DataSource`s (e.g. primary + read replica pools) is
  ever actually needed — that's the one capability neither Hikari nor a single per-pool bulkhead
  gives you for free, and would be the strongest argument for a bespoke shared-gate library instead
  of per-pool `@Bulkhead` annotations.
- No decision yet on packaging: a small standalone library (`stormgate-java`?) vs. just
  documentation/a recipe for wiring Resilience4j + Hikari + datasource-proxy together in a given app.
- Still unverified: real numbers from actually measuring the SQS-catch-up incident with all three
  timers in place, to confirm which of (indexes / connection burst / per-message commit / SQS
  consumption rate) is actually dominant before spending more design effort here.
