# pengdows.stormgate

A lightweight ADO.NET connection admission controller for .NET 8+.

---

## The Problem

When traffic spikes, every request tries to open a database connection at the same time.
The provider's connection pool can't queue fast enough. Threads pile up. The application
falls over.

This is a connection storm. StormGate stops it.

---

## How It Works

StormGate places a `SemaphoreSlim` gate in front of connection opens. At most
`maxConcurrentOpens` connections can be opened concurrently. If the gate can't be
acquired within `acquireTimeout`, a `TimeoutException` is thrown rather than letting
the caller pile up indefinitely.

The permit is tied to the connection. When the connection is closed or disposed —
however that happens — the permit is released automatically. No manual bookkeeping.

---

## Quickstart

```csharp
var gate = StormGate.Create(
    MySqlConnectorFactory.Instance,
    connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromMilliseconds(750));

await using var conn = await gate.OpenAsync();
// use conn with Dapper, raw ADO.NET, Hangfire, etc.
```

`StormGate` accepts any `DbProviderFactory`. It uses the provider's native `DbDataSource`
when one is available, and falls back to a generic wrapper when not.

---

## Public API

```
IConnectionFactory       — OpenAsync(CancellationToken) → DbConnection
StormGate                — Create(...) factory method, IConnectionFactory, IDisposable, IAsyncDisposable
```

Everything else is internal.

---

## What StormGate Is Not

StormGate is not a connection pool. It does not replace your provider's pool.

StormGate is not a retry library, an ORM, or a policy engine.

It does one thing: limit how many connections can be opened at once so a burst of
requests does not exhaust the pool before it has a chance to queue.

---

## Logging

Pass an `ILogger` to get operational visibility:

```csharp
var gate = StormGate.Create(
    factory, connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromMilliseconds(750),
    logger: loggerFactory.CreateLogger<StormGate>());
```

- **Warning** — logged when a permit times out (saturation signal)
- **Error** — logged when the underlying connection fails to open after a permit is acquired
- **Debug** — provider resolution and connection string normalization

The saturation warning is the key operational signal. If you see it, you are either
leaking connections, under-provisioned on `maxConcurrentOpens`, or the database itself
is the bottleneck.

---

## DI Registration

```csharp
services.AddSingleton<IConnectionFactory>(_ =>
    StormGate.Create(
        MySqlConnectorFactory.Instance,
        connectionString,
        maxConcurrentOpens: 32,
        acquireTimeout: TimeSpan.FromMilliseconds(750),
        logger: loggerFactory.CreateLogger<StormGate>()));
```

---

## This Is a Bandage

StormGate is a minimal stopgap. It will prevent connection storms and give you
operational breathing room.

When you are ready for proper connection governance — read/write lane separation,
writer starvation prevention, drain support, per-pool metrics, and support for
14 databases out of the box — migrate to [`pengdows.crud`](https://github.com/pengdows/pengdows.crud).

---

## Requirements

- .NET 8.0+
- `Microsoft.Extensions.Logging.Abstractions` 9.0+

---

## License

MIT
