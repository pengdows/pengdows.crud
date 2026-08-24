# pengdows.stormgate

[![NuGet](https://img.shields.io/nuget/v/pengdows.stormgate.svg)](https://www.nuget.org/packages/pengdows.stormgate)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![codecov](https://codecov.io/gh/pengdows/pengdows.crud/branch/main/graph/badge.svg)](https://codecov.io/gh/pengdows/pengdows.crud)

A lightweight ADO.NET connection admission controller for .NET 8+.

---

## The Problem

When traffic spikes, every request tries to open a database connection simultaneously. Even with a connection pool, the provider may struggle to queue fast enough, leading to thread pool starvation, high latency, or the "connection storm" that brings applications down.

The standard ADO.NET pool is excellent at managing idle connections, but it isn't designed to protect the database from an aggressive "thundering herd" of opening requests.

**StormGate stops the storm.**

---

## What StormGate Does *Not* Fix

StormGate gates how many connections may be *opening* at once. That's the right lever for a
real client-server database (SQL Server, PostgreSQL, MySQL, Oracle, etc.) whose server enforces
a connection limit and gets overwhelmed by a thundering herd of simultaneous opens.

It is **not** a fix for SQLite's (or any single-writer, file-based database's) write-locking
behavior. SQLite allows only one writer at a time against the file regardless of how many
connections are already open and idle — that's a write-serialization problem at the transaction
level, not a connection-admission problem. Gating opens does nothing for connections that are
already open and contending to write. If you're hitting `SQLITE_BUSY`/"database is locked"
errors, you need [**pengdows.crud**](https://github.com/pengdows/pengdows.crud)'s
`DbMode.SingleWriter` — a turnstile governor purpose-built to serialize write *tasks* against a
file-based SQLite/DuckDB database while still allowing fully concurrent reads.

---

## How It Works

StormGate places a `SemaphoreSlim` gate in front of your connection opens.

1.  **Gated Opens**: At most `maxConcurrentOpens` connections can be in the process of opening or being held by the application.
2.  **Backpressure**: If the gate cannot be acquired within the `acquireTimeout`, a `TimeoutException` is thrown immediately. This provides fast-fail backpressure instead of letting callers pile up indefinitely.
3.  **Automatic Release**: The permit is tied to the `DbConnection` wrapper. When the connection is closed or disposed — through any path — the permit is released back to the gate automatically.
4.  **Provider Aware**: It uses the provider's native `DbDataSource` when available (for features like prepared-statement caching) and falls back to a generic wrapper otherwise.

---

## Quickstart

```csharp
using pengdows.stormgate;
using MySqlConnector;

// 1. Create the gate (typically a singleton)
var gate = StormGate.Create(
    MySqlConnectorFactory.Instance,
    connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromMilliseconds(750));

// 2. Open a gated connection
await using var conn = await gate.OpenAsync();

// 3. Use conn with Dapper, raw ADO.NET, EF Core, etc.
// The permit is released when 'conn' is disposed or closed.
```

---

## Public API

```csharp
public interface IConnectionFactory
{
    // The core abstraction for obtaining a gated, opened connection
    Task<DbConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class StormGate : IConnectionFactory, IDisposable, IAsyncDisposable
{
    // Factory method to create a gate from a provider factory
    public static StormGate Create(
        DbProviderFactory factory,
        string connectionString,
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null);
}
```

---

## Logging & Observability

Pass an `ILogger` to get operational visibility:

```csharp
var gate = StormGate.Create(..., logger: loggerFactory.CreateLogger<StormGate>());
```

*   **Warning**: Logged when a permit times out (**Saturation Signal**). If you see this, you are either leaking connections, under-provisioned, or the database is the bottleneck.
*   **Error**: Logged when the underlying connection fails to open after a permit was successfully acquired.
*   **Debug**: Information about provider resolution and connection string normalization.

---

## Dependency Injection

```csharp
services.AddSingleton<IConnectionFactory>(_ =>
    StormGate.Create(
        SqlClientFactory.Instance,
        Configuration.GetConnectionString("Default"),
        maxConcurrentOpens: 32,
        acquireTimeout: TimeSpan.FromSeconds(1)));
```

## Entity Framework Core

StormGate governs a connection lease, so open the connection through the gate and give that
already-open connection to EF Core. Set `contextOwnsConnection: false`: the caller must dispose
the gated connection after the `DbContext` so the StormGate permit is returned.

This SQL Server example requires `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Data.SqlClient`,
and `pengdows.stormgate`:

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using pengdows.stormgate;

var gate = StormGate.Create(
    SqlClientFactory.Instance,
    connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromSeconds(1));

// Scope both objects to the database operation. Dispose the context first.
await using var connection = await gate.OpenAsync(cancellationToken);

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connection, contextOwnsConnection: false)
    .Options;

await using var db = new AppDbContext(options);
var customers = await db.Customers
    .Where(customer => customer.IsActive)
    .ToListAsync(cancellationToken);
```

`contextOwnsConnection: false` is essential here. With it, EF Core may close the supplied
connection as part of normal operation, but it does not dispose the wrapper; the outer
`await using` owns that disposal and releases the StormGate permit. Do not configure a shared
`AddDbContext` registration with a single gated connection — acquire one per unit of work instead.

For PostgreSQL, MySQL, or another provider, create the gate with that provider's
`DbProviderFactory` and replace `UseSqlServer` with its corresponding EF Core provider method.

**Not every EF Core provider tolerates this pattern.** `gate.OpenAsync()` returns a connection
wrapped in StormGate's own generic `DbConnection`/`DbCommand` types, not the provider's concrete
ones. Some providers' EF Core implementations cast the `DbCommand`/`DbDataReader`/`DbParameter`
they're handed back to their own concrete type somewhere in their real pipeline — that cast fails
against StormGate's wrapper the same way it would against any other non-native `DbConnection`
wrapper, in production against a real server, not just in tests. Confirmed for:

| Provider | Fails on |
| :--- | :--- |
| Oracle | any command at all |
| Db2 | any command at all |
| Npgsql (PostgreSQL) | `SaveChanges` (reader cast) |
| Firebird | any string-valued parameter |

SQLite, SQL Server, MySQL/MariaDB, and Snowflake do not cast to a concrete type and work fine
through this wrapper. If you're on one of the four providers above, use
`pengdows.stormgate.EntityFrameworkCore`'s `StormGateConnectionInterceptor` instead (see the link
below) — it never wraps the command/connection/reader pipeline, so it has no exposure to this
failure mode for any provider. See
[`EfProviderRawStormGateTests`](../pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests/EfProviderRawStormGateTests.cs)
for the reproduction of each row above.

**Using `AddDbContext`, `AddDbContextPool`, or `IDbContextFactory` instead of manual
per-operation connections?** See
[`pengdows.stormgate.EntityFrameworkCore`](../pengdows.stormgate.EntityFrameworkCore/README.md) —
it gates the same way via a `DbConnectionInterceptor`, composing with EF Core's own connection
management instead of requiring you to open and pass in connections by hand. It also avoids the
provider-casting gap described above entirely.

---

## When to use StormGate vs pengdows.crud

| Feature | StormGate | pengdows.crud |
| :--- | :--- | :--- |
| **Primary Goal** | Stop connection storms | High-performance SQL-first ORM |
| **Complexity** | Minimal (1 class) | Full-featured Framework |
| **Admission Control** | Single Global Gate | Read/Write Lane Separation |
| **Metrics** | Basic (Logging) | 36+ Detailed Metrics |
| **Multi-Dialect** | No (Provider Agnostic) | Yes (14+ DB specific optimizations) |
| **Legacy Apps** | **Perfect** (Dapper, etc.) | Requires migration |

---

## This Is a Bandage

StormGate is a minimal stopgap. It will prevent connection storms and give you operational breathing room in existing applications using Dapper, EF Core, or raw ADO.NET.

When you are ready for proper connection governance — including fairness, writer starvation prevention, drain support, and advanced type systems — migrate to [**pengdows.crud**](https://github.com/pengdows/pengdows.crud).

---

## Requirements

*   .NET 8.0+
*   `Microsoft.Extensions.Logging.Abstractions` 9.0+

---

## License

MIT

---

## Support

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-yellow?logo=buy-me-a-coffee)](https://buymeacoffee.com/pengdows)
