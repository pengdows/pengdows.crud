# pengdows.crud.opentelemetry

OpenTelemetry instrumentation for `pengdows.crud`. Provides deep observability into connection pooling, query performance, and database errors.

## Installation

Add the NuGet package to your project:

```bash
dotnet add package pengdows.crud.opentelemetry
```

## Setup

Register the instrumentation in your `Program.cs`:

```csharp
using pengdows.crud.opentelemetry;

// Standard OpenTelemetry metrics setup
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("pengdows.crud")
        .AddPrometheusExporter());

// Add pengdows.crud instrumentation
builder.Services.AddPengdowsTelemetry();
```

## Metrics

The meter is named `pengdows.crud` (`PengdowsMetricsObserver.MeterName`). The observer creates 53 instruments in total
(`PengdowsMetricsObserver.cs`): 48 under the `pengdows.db.client.` prefix and 5 OpenTelemetry semantic-convention
instruments named `db.client.*`.

### Core Metrics

| Name | Type | Description |
|------|------|-------------|
| `pengdows.db.client.connections.opened` | Counter | Total connections opened |
| `pengdows.db.client.connections.current` | Gauge | Current open connections |
| `pengdows.db.client.commands.executed` | Counter | Total commands executed successfully |
| `pengdows.db.client.commands.failed` | Counter | Total commands failed |
| `pengdows.db.client.command.duration.p95` | Gauge | Approximate P95 command duration (ms); needs `MetricsOptions.EnableApproxPercentiles` |
| `pengdows.db.client.transactions.active` | Gauge | Currently active transactions |

### All `pengdows.db.client.*` instruments

| Family | Instruments (suffix after `pengdows.db.client.`) | Type |
|--------|-----------|------|
| Commands | `commands.executed`, `commands.failed`, `commands.timed_out`, `commands.cancelled`, `commands.slow` | Counter (`{command}`) |
| Rows | `rows.read`, `rows.affected` | Counter (`{row}`) |
| Connections | `connections.opened`, `connections.closed`, `connections.long_lived` | Counter (`{connection}`) |
| Transactions | `transactions.committed`, `transactions.rolled_back` | Counter (`{transaction}`) |
| Errors | `errors.deadlocks`, `errors.serialization_failures`, `errors.constraint_violations` | Counter (`{error}`) |
| Statements and sessions | `statements.prepared`, `statements.evicted`, `session.inits` | Counter |
| Connection state | `connections.current`, `connections.peak`; `connections.hold_duration.avg`, `connections.open_duration.avg`, `connections.close_duration.avg` (ms) | Gauge |
| Command latency | `command.duration.p95`, `command.duration.p99`, `command.duration.avg`, `command.failed_duration.avg` (ms) | Gauge |
| Reader lease | `reader.time_to_first_row.avg`, `reader.consumption_duration.avg`, `reader.lease_duration.avg` (ms) | Gauge |
| Transaction state | `transactions.active`, `transactions.peak`; `transactions.duration.avg`, `transactions.duration.p95`, `transactions.duration.p99` (ms) | Gauge |
| Statements and sessions | `statements.cached`; `session.init_duration.avg` (ms) | Gauge |
| Pool state | `pool.slots.in_use`, `pool.slots.peak`, `pool.slots.queued`, `pool.slots.max`, `pool.turnstile.queued`; `pool.wait_duration_avg`, `pool.hold_duration.avg` (ms) | Gauge |
| Pool totals | `pool.acquired_total`, `pool.slot_timeouts_total`, `pool.turnstile_timeouts_total`, `pool.canceled_waits_total` | Observable counter |

Averages (`*.avg`) are exponentially weighted moving averages; the P95/P99 gauges are approximate and stay `0` until
percentile tracking is enabled and samples exist.

### Semantic-convention instruments

| Name | Type | Unit |
|------|------|------|
| `db.client.operation.duration` | Histogram | `s` (derived from `ActivitySource("pengdows.crud")` spans of tracked contexts) |
| `db.client.connection.count` | Observable up-down counter | `{connection}` (`db.client.connection.state` = `used`) |
| `db.client.connection.max` | Observable up-down counter | `{connection}` |
| `db.client.connection.pending_requests` | Observable up-down counter | `{request}` |
| `db.client.connection.timeouts` | Observable counter | `{timeout}` (slot plus turnstile timeouts) |

### Tags

The `pengdows.db.client.*` instruments carry:
* `db.name`: The value of `IDatabaseContext.Name`.
* `db.system`: The database product (e.g., `postgresql`, `sqlite`).
* `pool.label`: Present on the `pool.*` instruments only, with the value `reader` or `writer`.

The `db.client.*` semantic-convention instruments use the OpenTelemetry tag names instead:
* `db.client.operation.duration`: `db.system.name`, `db.namespace`, `db.operation.name`, and `error.type` on failed operations.
* `db.client.connection.*`: `db.client.connection.pool.name` (`<context name>.reader` or `<context name>.writer`).

## Multi-Tenancy

The instrumentation automatically discovers all `IDatabaseContext` instances registered in DI. For dynamic or late-bound tenant contexts, you can manually register them:

```csharp
// Inside your tenant resolver or factory
context.TrackPengdowsMetrics(serviceProvider);
```

## Resource Protection

This library is designed to avoid adding instrumentation work to the query hot path. Metrics are exported from `IDatabaseContext` metric snapshots and update events rather than by wrapping every call site with additional telemetry code.

## Support

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-yellow?logo=buy-me-a-coffee)](https://buymeacoffee.com/pengdows)
