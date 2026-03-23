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
