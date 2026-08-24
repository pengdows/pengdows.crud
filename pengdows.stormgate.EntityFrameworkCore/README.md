# pengdows.stormgate.EntityFrameworkCore

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

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

Two independent questions, two independent tiers — a provider can satisfy the first without
satisfying the second, and only the first matters for production use of this package:

- **Tier 1 — connection admission control (production-relevant).** Does the provider accept an
  externally-supplied `DbConnection`, and does `StormGateConnectionInterceptor` correctly gate its
  open/close lifecycle? `DbConnectionInterceptor` fires on ADO.NET connection events only — a
  layer no provider's command/parameter/reader handling can interfere with — so every provider
  that accepts an external connection at all satisfies this tier.
- **Tier 2 — unit-testable via `pengdows.crud.fakeDb` (testing-only, not a production concern).**
  Does the provider's real SQL generation, parameter binding, and `SaveChanges` pipeline also run
  correctly against a `fakeDb`-backed connection, with zero real database engine? This is a much
  stronger requirement than Tier 1, and every ❌ in the Tier 2 column below is a hardcoded
  `(ConcreteProviderType)genericDbObject` cast **inside that provider's own EF Core implementation
  code** — not anything StormGate, pengdows.crud, or fakeDb does. StormGate has no involvement in
  Tier 2 at all; fakeDb is a generic, provider-agnostic ADO.NET fake maintained by pengdows.crud,
  and this table merely *exposes*, through testing, a pre-existing fact about how each provider
  package happens to be written. It is **not a production concern either way**: a real database
  engine has no casting problem with itself, so every Tier-1 ✅ below is fully safe to run this
  package against in production regardless of its Tier 2 result.

| Provider | Tier 1 (admission control) | Tier 2 (fakeDb unit-testable) | Tier 2 caveat — cast inside that provider's own code |
|---|---|---|---|
| SQLite | ✅ | ✅ | — |
| SQL Server | ✅ | ✅ | — |
| MySQL / MariaDB (Pomelo) | ✅ | ✅ | — |
| Snowflake | ✅ | ✅ | — |
| PostgreSQL (Npgsql) | ✅ | ❌ | `SaveChanges` crashes — Npgsql's own `NpgsqlModificationCommandBatch.Consume` casts the reader to concrete `NpgsqlDataReader` |
| Firebird | ✅ | ❌ | any string-valued parameter crashes — FirebirdSql's own `FbStringTypeMapping.ConfigureParameter` casts to concrete `FbParameter` |
| Oracle | ✅ | ❌ | any command at all crashes — Oracle's own `OracleRelationalCommand.CreateDbCommand` casts to concrete `OracleCommand` |
| Db2 | ✅ | ❌ | any command at all crashes — IBM's own `Db2RelationalCommand.CreateDbCommand` casts to concrete `DB2Command` |
| DuckDB | ❌ | ❌ | no viable net8.0 DuckDB EF Core package exposes a `DbConnection`-accepting overload at all — fails Tier 1, so Tier 2 is unreachable |

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
using pengdows.stormgate.EntityFrameworkCore;

// One shared instance — see "Critical: Share One Instance" below.
var gate = new StormGateConnectionInterceptor(
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromSeconds(1));

services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseStormGate(gate));
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

## Critical: Share One Instance

`StormGateConnectionInterceptor` holds the semaphore that does the actual throttling. If a
fresh instance is constructed for every `DbContext`, each gets its own semaphore and nothing is
throttled across instances:

```csharp
// WRONG — options callback re-runs per request-scoped DbContext with plain AddDbContext,
// so every request gets its own interceptor and its own semaphore. No cross-request throttling.
services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseStormGate(maxConcurrentOpens: 32, acquireTimeout: TimeSpan.FromSeconds(1)));
```

```csharp
// RIGHT — one interceptor instance, shared across every DbContext it gates.
services.AddSingleton(new StormGateConnectionInterceptor(32, TimeSpan.FromSeconds(1)));

services.AddDbContext<AppDbContext>((provider, options) => options
    .UseSqlServer(connectionString)
    .UseStormGate(provider.GetRequiredService<StormGateConnectionInterceptor>()));
```

The convenience `UseStormGate(maxConcurrentOpens, acquireTimeout, logger?)` overload that
constructs an interceptor inline is only safe when the options-configuration delegate itself
runs once and is reused for every gated instance — e.g. `AddDbContextPool`'s shared options
template, or a process with exactly one long-lived `DbContext`.

## Testing Without a Real Database

Because `DbConnectionInterceptor` fires on `Open`/`Close`/failed-open for any `DbConnection`
EF Core is handed, the throttling logic itself is testable against a
[`pengdows.crud.fakeDb`](https://www.nuget.org/packages/pengdows.crud.fakeDb)-backed connection
instead of a real database engine — no real SQLite, no Testcontainers, entirely offline:

```csharp
var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
var interceptor = new StormGateConnectionInterceptor(maxConcurrentOpens: 1, acquireTimeout: TimeSpan.FromMilliseconds(150));

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

- **Per-process, not fleet-wide.** The semaphore caps concurrency within one `StormGateConnectionInterceptor`
  instance. Across N replicas, the database can still see up to N × `maxConcurrentOpens`
  connections. This bounds your blast radius per instance — it does not coordinate a global cap.
- **A `TimeoutException` is only a fix if something handles it.** Left unhandled, an
  admission-control failure just moves the storm from the database to your application. Pair
  this with retry/backoff or a circuit breaker at the call site.
- **Not a substitute for the provider's own pool sizing.** This bounds concurrently open/in-use
  connection leases at the application level; it doesn't replace `Max Pool Size` or other
  provider-level connection pool configuration.
- **Not a fix for SQLite (or any single-writer file database) locking.** This is the right tool
  for a client-server database whose server enforces a connection limit; it does nothing for
  `SQLITE_BUSY`/"database is locked" errors, which come from write contention on connections
  that are already open, not from too many opens. See the base
  [`pengdows.stormgate`](../pengdows.stormgate/README.md#what-stormgate-does-not-fix) README
  for that distinction and the actual fix (`pengdows.crud`'s `DbMode.SingleWriter`).

## When to Use `pengdows.stormgate` Instead

If you own the `DbConnection` directly — raw ADO.NET, Dapper, or an EF Core `DbContext`
constructed from an externally-owned connection via `UseSqlServer(connection, contextOwnsConnection: false)`
— the base `pengdows.stormgate` package's `StormGate` wrapper works without needing EF Core as
a dependency at all. Reach for this package specifically when EF Core owns the connection
lifecycle itself, via `AddDbContext`, `AddDbContextPool`, or `IDbContextFactory`.
