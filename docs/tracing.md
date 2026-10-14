# Tracing (`ActivitySource`)

pengdows.crud emits its own OpenTelemetry-compatible tracing spans from the core library — **no extra package required**. This is distinct from `pengdows.crud.opentelemetry` (see `docs/opentelemetry-metrics.md`), which exports metrics (counters/histograms), not trace spans. The wiki's `v2-Diagnostics-and-Telemetry` page already showed the `.AddSource("pengdows.crud")` line in an example, but never explained what it actually captures — this doc fills that in.

## Enabling it

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddSource("pengdows.crud"));   // this line, specifically
```

That's the entire setup. Every real command execution — `ExecuteNonQueryAsync`, and every scalar/reader method, since they all route through the same two internal execution paths — creates a span (`ActivitySource("pengdows.crud", "2.0.1")`, `SqlContainer.cs` — note the version string is a hardcoded literal, not read from the assembly, so it does not track the package version).

## What each span carries

| Tag | Contains |
|---|---|
| `db.system` | Detected product, lowercased (e.g. `"postgresql"`) |
| `db.name` | The owning context's name |
| `db.statement` | The query text as built — parameterized, never interpolated literal values; truncated to 4000 characters with a `...(truncated)` suffix |
| `db.operation` | `"ExecuteNonQuery"` or `"ExecuteReader"` |
| `pengdows.context_id` | The owning `DatabaseContext`'s root ID |
| `db.rows_affected` | Rows affected; set only on a successful `ExecuteNonQuery` span |

Spans are `ActivityKind.Client`. A successful command ends with status `Ok`; a failure ends with status `Error` carrying no description, plus a sanitized `exception` event (exception type and a message truncated to 1000 characters, never the stack trace), so raw provider messages do not reach trace storage by a second path (`SqlContainer.cs`, CORE-024).

`pengdows.context_id` is the same tag `pengdows.crud.opentelemetry`'s metrics adapter uses internally to filter spans when deriving its `db.client.operation.duration` histogram — useful for correlating a trace span with which `DatabaseContext` instance produced it in a multi-context (e.g. multi-tenant) application, without needing that separate package installed just to get the correlation ID.

## Combining with provider-level instrumentation

Because pengdows.crud uses standard ADO.NET providers, you can layer this with `OpenTelemetry.Instrumentation.SqlClient`/`.Npgsql`/etc. for provider-level spans (connection-level detail specific to that driver) alongside pengdows.crud's own operation-level spans:

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddSource("pengdows.crud")
        .AddSqlClientInstrumentation()
        .AddNpgsqlInstrumentation());
```

The two are complementary, not redundant: pengdows.crud's spans carry the parameterized SQL text and the owning-context correlation ID; provider instrumentation carries driver-specific connection detail pengdows.crud doesn't expose.
