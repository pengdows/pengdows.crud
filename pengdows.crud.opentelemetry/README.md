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

All metrics are prefixed with `pengdows.db.`.

### Core Metrics

| Name | Type | Description |
|------|------|-------------|
| `pengdows.db.client.connections.opened` | Counter | Total connections opened |
| `pengdows.db.client.connections.current` | Gauge | Current active connections |
| `pengdows.db.client.commands.executed` | Counter | Total successful commands |
| `pengdows.db.client.commands.failed` | Counter | Total failed commands |
| `pengdows.db.client.command.duration.p95` | Gauge | P95 command latency (ms) |
| `pengdows.db.client.transactions.active` | Gauge | Current active transactions |

### Tags

All metrics include the following tags:
* `db.name`: The value of `IDatabaseContext.Name`.
* `db.system`: The database product (e.g., `postgresql`, `sqlite`).
* `pool.label`: Present on pool metrics with values such as `reader` or `writer`.

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
