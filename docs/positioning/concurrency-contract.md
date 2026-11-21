# pengdows.crud — Concurrency Contract

This document is the deeper reference behind [`product-thesis.md`](./product-thesis.md)'s
principles 2 and 5: a falsifiable, per-guarantee breakdown of what pengdows.crud's connection
governance actually promises under concurrent load, each backed by a specific regression test
rather than a general assurance (principle 10). Treat the guarantees below as the current,
tested contract; for architectural *why*, see the product thesis; for exact test-file citations
across the whole library, see [`implementation-evidence.md`](./implementation-evidence.md).

Last verified: 2026-11-17, against branch `2.0.6`. Every guarantee below has at least one
passing test backing it; two genuine bugs were found and fixed while building this contract,
not just theoretical gaps closed on paper — both are told in full below, because finding real
bugs by testing adversarially is itself evidence for principle 10's claim, not something to
quietly omit.

## How this is verified

Two complementary test layers, used deliberately for what each is actually good at:

- **`pengdows.crud.fakeDb`**, a complete fake ADO.NET provider, for deterministic races that
  real concurrency would only reproduce by luck. A small number of production-only test hooks
  (`internal Action? TestOnlyXxx`, null on every real path, invoked at one precise point) let a
  test force an exact interleaving — a release racing an in-progress attach, a reader's
  continuation stalling mid-release — on command, in one thread, instead of hammering the real
  mechanism thousands of times and hoping to hit it.
- **Real provider integration tests** (Docker containers, not fakeDb) wherever the claim is
  about what a real database actually does — a mock agreeing with itself proves nothing.
  Cancellation (below) and read-only enforcement were each confirmed this way before being
  pinned at the unit level for regression coverage.

## Admission control

Every `DatabaseContext` runs up to two independent admission governors — one for reads, one
for writes — each a semaphore-based gate with its own permit count, queue, and timeout.

- **The admission bound holds under real concurrent load, not just in isolation.** A governor
  never admits more checkouts than its configured limit, verified with 16 workers racing
  200 iterations each of cancel/grant/resize against a 2-slot gate, asserting the in-flight
  count never exceeds the limit on any successful acquire.
- **A queue position is released exactly once, even when cancellation and a grant race each
  other.** The permit either reaches the caller or returns to the gate — never both, never
  neither — proven under exactly that race, including a concurrent `Dispose`.
- **Saturating the read side never blocks the write side, and vice versa.** With every read
  permit held and the read queue also full, a writer with a free write permit is admitted
  immediately — not delayed by read-side contention it has no dependency on. Independent
  reader/writer admission is also what makes a read-replica topology possible without
  application-level routing plumbing (principle 5's read/write-split capability).

## Resource lifecycle

- **Every governed checkout releases its permit exactly once, on every exit path** — success,
  exception, cancellation, and `Dispose`/`DisposeAsync` alike.
- **A transaction acquires its governed connection once, at `BeginTransaction`, and holds it for
  the transaction's entire life** — not once per command inside it. Every statement in the
  transaction runs on that same pinned connection from begin to commit or rollback.
- **A reader's connection closes exactly when its lease ends** (on `Dispose`, or when `Read`
  returns false for a standalone read) and stays open for the whole lease when one is already
  owed elsewhere — inside a transaction, on a persistent single connection, or when pinned for
  a generated-key follow-up query on the same physical connection the `INSERT` used.

**A real bug, found by this testing and fixed, not a hypothetical.** The connection-to-permit
attach step had two silent failure modes: attaching a second permit to an already-attached
connection silently overwrote the first (leaking it), and attaching a permit after the
connection had already released its slot silently re-attached to a dead state. No production
call site is known to reach either case — every governed checkout attaches in its constructor,
and the only late-attach sites run during single-threaded context initialization — but "no
known call site" is a different claim from "structurally impossible," and a library this deep
in connection-lifetime correctness shouldn't rely on the former when the latter is enforceable.
Fixed: the attach step now throws immediately on either invalid transition and returns the
rejected permit to its governor rather than silently corrupting state, including the harder
case of a release racing an attach already in progress.

## SingleWriter write serialization

The property benchmarked in the product thesis's opening claim — a pengdows.crud writer never
contends for SQLite's lock with another pengdows.crud writer, because admission happens before
either reaches the database — rests on two further guarantees, both regression-tested:

- **At most one write-intent checkout is admitted at a time under `SingleWriter`.**
- **The writer-preference turnstile (principle 5) genuinely blocks new read admission while a
  writer is active or waiting**, confirmed by holding a writer and showing a new reader blocked,
  then timed out, then admitted immediately once the writer releases — not merely
  slow-scheduled, a distinction a test proved by also showing the inverse: with no writer
  active, two writes mistakenly sent with *read* intent run fully concurrently through the
  reader governor, the exact danger the turnstile exists to prevent once a real writer shows up.

**One limitation stated precisely rather than glossed over.** Serialization applies to write
*intent*, not to SQL shape: a modifying statement sent through a read-intent call bypasses write
serialization unless a writer is concurrently active (where the turnstile catches it anyway) or
the connection itself is independently read-only (where the database rejects it — see below).
Choosing the correct execution intent for modifying SQL remains the caller's responsibility;
this is a limitation of the underlying model this library makes precise and testable, not one
it silently hides.

## Cancellation

A caller's cancellation is guaranteed to surface as `OperationCanceledException` — never masked
as a generic provider failure, even when the underlying driver itself gets this wrong.

**A real bug, found by testing a second provider and fixed, not a hypothetical.** Running the
identical "cancel a running command, confirm the server actually stopped it" test against two
live databases found that PostgreSQL honored the contract cleanly, but `Microsoft.Data.SqlClient`
reported a server-cancelled SQL Server command as its own `SqlException` ("...Operation
cancelled by user.") instead of `OperationCanceledException` — even though SQL Server itself had
genuinely stopped the query server-side (confirmed via `sys.dm_exec_requests`). Fixed: the
exception-translation path now recognizes this exact shape and rethrows the real exception type,
with the original preserved as `InnerException` — gated on the caller's own token actually
having been canceled, so an unrelated exception with similar wording is never misreported as
this caller's cancellation. Pinned at the unit level via a fake-provider capability so the
regression doesn't need a live container to catch again.

## Pool-acquire timeout correctness

`PoolAcquireTimeout` bounds how long a caller waits for a permit before `PoolSaturatedException`.
.NET has two idioms for "no timeout" — `TimeSpan.MaxValue` and `Timeout.InfiniteTimeSpan` — and
both now behave the same way: as a bound so far in the future it is functionally unbounded for
any real caller.

**A real bug, found by testing a deliberately adversarial input and fixed, not a hypothetical.**
The internal conversion from a configured timeout to a stopwatch-tick budget correctly clamped
`TimeSpan.MaxValue` to a very large but finite bound, but collapsed `Timeout.InfiniteTimeSpan`'s
negative duration to roughly one tick — a caller asking to wait forever would instead time out
in about a microsecond, the opposite of what was asked. A pre-existing unit test had pinned that
exact collapse as intended behavior; it was wrong, and has been corrected alongside the fix. Not
reachable through the library's own public configuration validation today (a configured
`PoolAcquireTimeout` below or at zero is rejected before construction), but a defect in
unexported internal code is still a defect, not merely theoretical.

## Read-only enforcement

`ExecutionType.Read` is routing intent, not database policy by itself (principle 4's three-layer
model: the library's own execution-intent guard, which covers every database; real connection or
session-level database enforcement, where the engine supports it; and transaction-level
enforcement for engines whose read-only mode doesn't persist past one transaction). Every
database pengdows.crud supports now has its own regression test confirming which layer actually
protects it — including the honest exceptions, where the library applies the right
marker/SQL and the real database still doesn't act on it (Spanner, SQL Server, SingleStore, and
Firebird, each live-confirmed). See
[`implementation-evidence.md`](./implementation-evidence.md#mysql-vs-mariadb-read-only-session-sql-principle-4)
for the full per-database table and test citations — kept there rather than duplicated here
since it is the kind of detail that changes independently of this contract's structure.

## Counters are exact holder counts; drain also waits for in-flight releases

`NumberOfOpenConnections`, `PeakInUse`, and the pool-statistics snapshot's `InUse` count the permit
holders. A holder leaves the count before its permit returns to the gate, so a new holder that takes
the returned permit is never counted alongside the one that gave it back, and these counters do not
exceed the configured limit from that cause. They are still diagnostic: admission never reads them,
and the admission guarantees above are checked against `PoolSaturatedException` behavior itself.

Drain is separate. A release that has started but not finished (still touching the semaphore or the
turnstile) keeps drain incomplete, so a disposal never races a release.

## What this document does not claim

- **Bounded write latency.** Admission control bounds how many writers are admitted, not how
  long a lock wait, disk I/O, or query execution takes once admitted.
- **Full database-level read-only enforcement everywhere.** Several supported databases have no
  server-side mechanism at all (Snowflake, Db2, Sybase ASE, InterBase, TiDB) or apply one the server
  ignores (Spanner, SQL Server, SingleStore, Firebird) — the library's own execution-intent guard
  is what protects those, not the database, and that distinction is stated precisely above and in
  `implementation-evidence.md` rather than folded into a single "read-only is enforced" claim.
- **Noisy-neighbor isolation beyond admission state.** Per-tenant admission control is real and
  automatic (principle 3 combined with principle 5), but tenants sharing a physical server,
  provider connection pool, CPU, I/O, network, or database-level locks can still contend there —
  what's isolated is specifically the admission-control state this architecture owns.
- **Real OS/CLR thread-pool starvation, reproduced on demand.** One guarantee (writer-preference
  fairness under a stalled reader continuation) is proven via a deterministic test hook that
  forces the exact interleaving a starved thread pool would produce by luck; reproducing genuine
  thread-pool starvation itself in a regression suite was deliberately not attempted, because
  doing so reliably would mean testing the CLR scheduler's own documented behavior, not this
  library's logic.
