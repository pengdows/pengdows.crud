# pengdows.stormgate.EntityFrameworkCore

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../LICENSE)

Entity Framework Core connection admission control via a `DbConnectionInterceptor`.

---

## The Problem

`pengdows.stormgate` gates connection opens by wrapping the `DbConnection` itself — great for
raw ADO.NET, Dapper, or anywhere you own the connection's lifetime directly. But EF Core
usually doesn't hand you that opportunity: `AddDbContext`, `AddDbContextPool`, and
`IDbContextFactory<TContext>` all create and manage `DbConnection` instances internally from a
connection string. There's no natural place to substitute a wrapped connection without
abandoning the ergonomics (and, for pooling, the performance) those APIs provide.

**This package gates the same way, at the point EF Core actually opens connections — without
requiring you to hand-manage connection objects.**

## Compatibility

- .NET 8: EF Core 8.0.11 through EF Core 9.x
- .NET 10: EF Core 10.0.8 through EF Core 10.x

## Provider Compatibility

Two independent questions — a provider can satisfy the first without satisfying the second, and
only the first matters for production use of this package:

- **Production (connection admission control).** Does the provider accept an
  externally-supplied `DbConnection`, and does `StormGateConnectionInterceptor` correctly gate its
  open/close lifecycle? `DbConnectionInterceptor` fires on ADO.NET connection events only — a
  layer no provider's command/parameter/reader handling can interfere with — so every provider
  that accepts an external connection at all satisfies this column.
- **fakeDb-testable (testing-only, not a production concern).**
  Does the provider's real SQL generation, parameter binding, and `SaveChanges` pipeline also run
  correctly against a `fakeDb`-backed connection, with zero real database engine? This is a much
  stronger requirement than the Production column, and every ❌ below is a hardcoded
  `(ConcreteProviderType)genericDbObject` cast **inside that provider's own EF Core implementation
  code** — not anything StormGate, pengdows.crud, or fakeDb does. StormGate has no involvement in
  fakeDb-testability at all; fakeDb is a generic, provider-agnostic ADO.NET fake maintained by
  pengdows.crud, and this table merely *exposes*, through testing, a pre-existing fact about how
  each provider package happens to be written. It is **not a production concern either way**: in
  normal production use, the provider's own ADO.NET implementation creates the concrete command,
  parameter, and reader instances it then casts back to, so those casts always succeed — the
  failure mode only exists when something other than that provider's own driver constructs the
  object, which fakeDb does deliberately and a real connection never would. Every ✅ in the
  Production column below is fully safe to run this package against in production regardless of
  its fakeDb-testable result.

| Provider | Production (admission control) | fakeDb-testable | fakeDb-testable caveat — cast inside that provider's own code |
|---|---|---|---|
| SQLite | ✅ | ✅ | — |
| SQL Server | ✅ | ✅ | — |
| MySQL / MariaDB (Pomelo) | ✅ | ✅ | — (verified on net8.0 only: Pomelo has no EF Core 10-compatible release, so the net10.0 test leg omits MySQL/MariaDB) |
| Snowflake | ✅ | ✅ | — |
| PostgreSQL (Npgsql) | ✅ | ❌ | `SaveChanges` crashes — Npgsql's own `NpgsqlModificationCommandBatch.Consume` casts the reader to concrete `NpgsqlDataReader` |
| Firebird | ✅ | ❌ | any string-valued parameter crashes — FirebirdSql's own `FbStringTypeMapping.ConfigureParameter` casts to concrete `FbParameter` |
| Oracle | ✅ | ❌ | any command at all crashes — Oracle's own `OracleRelationalCommand.CreateDbCommand` casts to concrete `OracleCommand` |
| Db2 | ✅ | ❌ | any command at all crashes — IBM's own `Db2RelationalCommand.CreateDbCommand` casts to concrete `DB2Command` |
| DuckDB (`EnergyExemplar.EntityFrameworkCore.DuckDb`) | ✅ | not fakeDb-testable | `UseDuckDb` has no `DbConnection`-accepting overload, so this package's fakeDb-driven tests can't reach it at all — that's a testing-method limitation, not a StormGate incompatibility. Its `UseDuckDb` is a thin layer over `Microsoft.EntityFrameworkCore.Sqlite`: the object EF Core actually opens/closes is a real `Microsoft.Data.Sqlite.SqliteConnection`, the same connection type already proven fully Production and fakeDb-testable above. Confirmed directly against a real embedded DuckDB engine (no Docker, no fakeDb) — see `DuckDbInterceptorRealProviderTests`. |
| DuckDB (`DuckDB.EFCore`, denis-ivanov, net10.0-only) | ✅ | ❌ | A second, independent DuckDB provider. Unlike EnergyExemplar's, its `UseDuckDB` DOES accept an arbitrary `DbConnection` — so it was actually tried against fakeDb, and failed: `DuckDBRelationalConnection.OpenDbConnectionAsync` casts the incoming connection to concrete `DuckDB.NET.Data.DuckDBConnection` unconditionally, throwing on `Open()` itself (one layer earlier than the command-creation-time casts above). Confirmed directly against a real embedded DuckDB engine (no Docker, no fakeDb) that StormGate still governs it correctly — see `DuckDBEFCoreInterceptorRealProviderTests`. |
| DuckDB (`DuckDB.EFCoreProvider`, skuirrels, net10.0-only) | ✅ | ✅ | A **third**, independent DuckDB provider. As of 1.25.0 it added support for a caller-registered `DbProviderFactory` (`AddEntityFrameworkDuckDB()` calls `TryAddSingleton<DbProviderFactory>(...)`, so a caller-registered factory wins), and `UseDuckDB(connection, contextOwnsConnection: false)` accepts an arbitrary `DbConnection` end to end through queries, `SaveChanges`, and `EnsureCreated`. Confirmed here via `EfProviderCompatibilityTests`/`EfProviderDeepTests`/`EfProviderTypeMatrixTests`/`EfProviderTransactionTests`/`EfProviderBatchingTests`, all passing live against a real fakeDb connection — the only one of the three DuckDB providers wired into those shared theories (see `EfProviders.cs`). Its own functional-test suite independently depends on `pengdows.crud.fakeDb` to validate the same substitution path. **Dependency note:** it pulls in `Skuirrels.DuckDB.NET.Data.Full`, a fork that ships assemblies at the same path as the official `DuckDB.NET.Data.Full` already referenced for the other two DuckDB providers above — NuGet resolves this to one physical assembly per project rather than a conflict, and every DuckDB-related test in this repo's own multi-provider test project passes against whichever one wins; see that project's `.csproj` comment for detail. |

See `pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests/EfProviders.cs` for how each row
was verified (direct reproduction against the real provider package, not assumed) and the exact
line of that provider's own source that throws.

## How It Works

`StormGateConnectionInterceptor` implements `DbConnectionInterceptor`, EF Core's official
extension point for connection lifecycle events. It fires on every real physical
`Open`/`Close`/failed-open, regardless of how the owning `DbContext` was created or pooled —
so it composes with `AddDbContext`, `AddDbContextPool`, and `IDbContextFactory` alike.

A permit is acquired when a connection opens and held until it closes, fails to open, is
disposed, or transitions to `Broken` — not released the instant `Open()` returns. So this bounds
**concurrently open/in-use connection leases**, not merely simultaneous open attempts: a
connection an application holds open for a long-running unit of work occupies its permit for
that entire duration, the same as if it were still in the middle of opening.

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using pengdows.stormgate;
using pengdows.stormgate.EntityFrameworkCore;

// The StormGate — not the interceptor — holds the admission budget. See
// "Critical: Share One StormGate" below.
var stormGate = StormGate.Create(
    SqlClientFactory.Instance,
    connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromSeconds(1));

var interceptor = new StormGateConnectionInterceptor(stormGate);

services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseStormGate(interceptor));
```

When the gate is saturated, `OpenAsync()`/`Open()` throws `TimeoutException` instead of
queuing indefinitely or letting every caller pile onto the database at once. Callers are
expected to handle that — retry with backoff, return a 503, trip a circuit breaker — the point
is failing fast instead of contributing to the storm.

**The exact exception type your code sees can depend on the provider.** SQL Server's *default*
(non-retrying) execution strategy classifies `TimeoutException` as transient-looking and wraps
it in its own `InvalidOperationException` (suggesting `EnableRetryOnFailure`) rather than letting
it propagate raw — confirmed by running this interceptor against every EF Core provider this
package has been tested with (SQL Server, PostgreSQL, MySQL, MariaDB, Oracle, Firebird,
Snowflake, Db2). If you catch by exact type, catch `TimeoutException` *or* check
`ex.InnerException`/`ex.Message` for one whose message contains "storm gate" — don't assume the
saturation exception always propagates unwrapped.

**If you've opted into `EnableRetryOnFailure`, it will retry into a saturated gate, not fail
fast.** EF Core's built-in retrying execution strategies (confirmed for SQL Server's
`SqlServerRetryingExecutionStrategy`) classify a raw `TimeoutException` as transient and retry
the *entire operation* — including re-acquiring a StormGate permit — up to `maxRetryCount` times
before giving up and throwing `Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException`.
Against a gate that stays saturated, every one of those retries hits the same wall: each one
waits out `acquireTimeout` again, logs its own saturation warning, and gets nothing for it. This
isn't a bug in StormGate — it's exactly how EF classifies `TimeoutException` — but it means
fail-fast and `EnableRetryOnFailure` are in tension: the gate's whole purpose is to reject excess
load immediately rather than let it queue, and a retry policy re-introduces queuing behavior on
top of that rejection. If you use both together, size `maxRetryCount`/`maxRetryDelay`
deliberately (a real transient network blip and a saturated gate look identical to the retry
strategy), and remember worst-case latency for one call is now roughly
`(maxRetryCount + 1) × acquireTimeout` plus the retry delays. See
`EfRetryStrategyTests.SqlServerRetryingExecutionStrategy_TreatsSaturationTimeoutAsTransient_AndRetriesUntilExhausted`
for the confirmed reproduction.

**A saturated gate doesn't always mean some other caller is competing with you — it can be a
request blocking on a permit it already holds itself.** The scenario above assumes an unrelated
caller occupies the budget. But if your own code holds one connection open (e.g. an explicit
`Database.OpenConnectionAsync()`, or an outer unit of work) and, before closing it, triggers a
*separate* `EnableRetryOnFailure`-wrapped operation that needs its own connection, that second
operation is competing for a permit against a lease your own request is still holding. Retrying
doesn't help — every attempt just re-times-out against the same self-imposed saturation, exactly
like the external-caller case above but with no other traffic involved. This is a sizing problem
(see Limits below), not a StormGate or EF Core bug: `maxConcurrentOpens` must cover the largest
number of connection leases *one logical request* can legitimately hold at once, not just how
many requests you expect concurrently.

## Critical: Share One StormGate

The admission budget — the semaphore that does the actual throttling — lives on the
`StormGate` instance, not on `StormGateConnectionInterceptor`. The interceptor is a thin
adapter: constructing a fresh one is cheap and harmless as long as every interceptor
consumes permits from the **same** `StormGate`. What must never happen is constructing a
fresh `StormGate` per `DbContext` — that gives each one its own independent budget and
throttles nothing across instances:

```csharp
// WRONG — options callback re-runs per request-scoped DbContext with plain AddDbContext,
// so every request builds its own StormGate and its own budget. No cross-request throttling.
services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseStormGate(new StormGateConnectionInterceptor(
        StormGate.Create(SqlClientFactory.Instance, connectionString, 32, TimeSpan.FromSeconds(1)))));
```

```csharp
// RIGHT — one StormGate (and, for convenience, one interceptor built from it), shared across
// every DbContext it gates. Register the StormGate as a singleton so it, and its budget,
// outlive any individual request/DbContext.
services.AddSingleton(_ => StormGate.Create(
    SqlClientFactory.Instance, connectionString, maxConcurrentOpens: 32, acquireTimeout: TimeSpan.FromSeconds(1)));
services.AddSingleton(provider =>
    new StormGateConnectionInterceptor(provider.GetRequiredService<StormGate>()));

services.AddDbContext<AppDbContext>((provider, options) => options
    .UseSqlServer(connectionString)
    .UseStormGate(provider.GetRequiredService<StormGateConnectionInterceptor>()));
```

**This is also what lets EF Core and raw ADO.NET (e.g. Dapper) share one admission budget
against the same database**: register the same `StormGate` singleton, use it directly for
raw `StormGate.OpenAsync()` calls, and pass it to `StormGateConnectionInterceptor` for EF
Core — both consume permits from the same underlying gate.

## Hard physical connection ceilings

StormGate gates the lifetime of each EF Core `DbConnection`: the interceptor acquires a
permit when EF opens the connection and releases it when the connection closes, fails to
open, or is disposed. A shared gate therefore gives EF Core, raw ADO.NET, and Dapper a
deterministic limit on concurrently admitted connection lifetimes.

That is different from a hard limit on the total number of physical connections visible to
the database server. A provider pool may retain an idle physical connection after EF has
closed its logical connection and StormGate has released its permit. StormGate does not
rewrite provider connection strings, disable provider pooling, or change an externally
supplied `DbDataSource`/`DbConnection`.

If the database has a strict connection ceiling — especially in tests or a small
container — configure pooling deliberately. For the clearest hard ceiling, disable the
provider pool and let the shared StormGate budget control the live connections:

```csharp
var connectionString = new NpgsqlConnectionStringBuilder(rawConnectionString)
{
    Pooling = false
}.ConnectionString;

services.AddSingleton(_ => StormGate.Create(
    NpgsqlFactory.Instance,
    connectionString,
    maxConcurrentOpens: 20,
    acquireTimeout: TimeSpan.FromMinutes(5)));
```

Use that same effective connection string when configuring EF Core. If provider pooling
remains enabled, size both the provider pool and the StormGate budget, and account for
idle pooled connections, administrative connections, and every application replica.
`maxConcurrentOpens` is a per-process admission budget; it is not a server-wide pool-size
setting and StormGate does not automatically set `Pooling=false`.

## Test EF Core without a database

Microsoft's EF Core documentation is explicit about the trade-offs here. The original
guidance is worth reading directly:

- [Choosing a testing strategy](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)
  explains why a fake provider or SQLite cannot reliably reproduce production-provider
  behavior, recommends coverage against the real database, and discusses the repository
  alternative.
- [Testing without your production database system](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database)
  documents the available test-double approaches and their limitations.
- [Testing EF Core applications](https://learn.microsoft.com/en-us/ef/core/miscellaneous/testing/sqlite)
  summarizes the database-versus-test-double decision and calls out differences in
  transactions, raw SQL, and provider behavior.

The guidance strongly discourages the EF Core InMemory provider for general testing; this
README does not claim that a fake ADO.NET provider replaces provider-specific integration
tests.

### What StormGate makes possible

This is not a claim that stock EF Core can be made provider-accurate by swapping in its
InMemory provider. The distinctive value of `StormGate.EntityFrameworkCore` is that it
uses EF Core's real `DbConnectionInterceptor` lifecycle together with the ADO.NET test
provider from [`pengdows.crud.fakeDb`](https://www.nuget.org/packages/pengdows.crud.fakeDb).
That gives applications using a provider which accepts an externally supplied
`DbConnection` a meaningful offline test path:

```csharp
var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(factory.CreateConnection()!, contextOwnsConnection: false) // or your provider's external-DbConnection overload
    .UseStormGate(interceptor)
    .Options;
```

Here `fakeDb` supplies the ADO.NET `DbConnection`, commands, parameters, readers,
transactions, and failure behavior; EF Core and the selected relational provider still own
their normal connection lifecycle; and StormGate supplies connection admission control.
The test exercises the actual EF Core connection-opening/closing path and the StormGate
interception boundary without a database. It is materially different from evaluating LINQ
against an in-memory collection or using EF Core's InMemory provider.

This is the practical wedge into the wider [`pengdows.crud`](https://www.nuget.org/packages/pengdows.crud)
toolkit: `fakeDb` is the reusable ADO.NET test double, while `pengdows.crud` provides the
SQL-first database access, connection management, transactions, tenancy, and provider
realities that EF Core deliberately leaves to its providers and application architecture.

This is not a replacement for provider-specific integration tests. SQL translation,
database locking, isolation, server limits, and dialect behavior still require the actual
database engine. The value is that connection-lifecycle and pool-protection tests no longer
need to start a database merely to prove that StormGate and EF Core cooperate correctly.

## Testing Without a Real Database

Because `DbConnectionInterceptor` fires on `Open`/`Close`/failed-open for any `DbConnection`
EF Core is handed, the throttling logic itself is testable against a
[`pengdows.crud.fakeDb`](https://www.nuget.org/packages/pengdows.crud.fakeDb)-backed connection
instead of a real database engine — no real SQLite, no Testcontainers, entirely offline:

```csharp
var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));
var interceptor = new StormGateConnectionInterceptor(stormGate);

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(factory.CreateConnection()!, contextOwnsConnection: false)
    .UseStormGate(interceptor)
    .Options;

await using var context = new AppDbContext(options);
await context.Database.OpenConnectionAsync();   // real interceptor logic, no real database
```

See `pengdows.stormgate.EntityFrameworkCore.Tests/StormGateConnectionInterceptorFakeDbTests.cs`
for the full pattern, including simulating a physical open failure via
`fakeDbConnection.BreakConnection(skipFirst: true)` to verify the permit is still released.

## Limits

- **Size for concurrent leases per logical request, not concurrent inbound requests.**
  `maxConcurrentOpens` bounds concurrently *held* connection leases, not HTTP/RPC request
  concurrency — those aren't the same number. A single logical request can hold more than one
  lease at once: an explicitly-opened connection plus a separately-opened one for a nested
  operation, or an `EnableRetryOnFailure` attempt that opens a fresh connection before an earlier
  one on the same request has closed. Size the budget for the worst case of how many leases one
  request may hold concurrently, multiplied by your expected request concurrency — undersizing it
  doesn't just reject other callers under load, it can make a request wait on a permit only its
  own earlier lease is holding. See the `EnableRetryOnFailure` note above for the shape this
  takes.
- **Per-process, not fleet-wide.** The semaphore caps concurrency within one `StormGate`
  instance. Across N replicas, the database can still see up to N × `maxConcurrentOpens`
  connections. This bounds your blast radius per instance — it does not coordinate a global cap.
- **A `TimeoutException` is only a fix if something handles it.** Left unhandled, an
  admission-control failure just moves the storm from the database to your application. Pair
  this with retry/backoff or a circuit breaker at the call site.
- **Not a substitute for the provider's own pool sizing.** This bounds concurrently open/in-use
  connection leases at the application level; it doesn't replace `Max Pool Size` or other
  provider-level connection pool configuration. For the distinction between admitted
  connection lifetimes and total physical server connections, see
  [Hard physical connection ceilings](#hard-physical-connection-ceilings).
- **Not a fix for SQLite (or any single-writer file database) locking.** This is the right tool
  for a client-server database whose server enforces a connection limit; it does nothing for
  `SQLITE_BUSY`/"database is locked" errors, which come from write contention on connections
  that are already open, not from too many opens. See the base
  [`pengdows.stormgate`](../pengdows.stormgate/README.md#what-stormgate-does-not-fix) README
  for that distinction and the actual fix (`pengdows.crud`'s `DbMode.SingleWriter`).

## Relationship to `pengdows.stormgate`

This package depends on the base [`pengdows.stormgate`](../pengdows.stormgate/README.md)
package — `StormGate` is where the admission budget actually lives;
`StormGateConnectionInterceptor` is the adapter that lets EF Core's own connection lifecycle
consume permits from it, since EF Core owns connection creation internally and gives you no
`DbConnection` to wrap directly the way raw ADO.NET/Dapper access does.

**If EF Core is the only way your process talks to the database**, you still need to
construct a `StormGate` (it requires a `DbDataSource`/connection string even though this
package never calls `StormGate.OpenAsync()`), but you'll likely never call any of its other
members directly — just hand it to `StormGateConnectionInterceptor` and register both as
singletons, as shown above.

**If your process also talks to the same database via raw ADO.NET or Dapper**, share the
*same* `StormGate` singleton between that code (calling `StormGate.OpenAsync()` directly) and
the EF Core interceptor above — that is what makes "one database, one admission budget" true
across both access paths, rather than each maintaining an independent one.
