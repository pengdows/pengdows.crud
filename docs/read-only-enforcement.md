# Read-Only Enforcement

`pengdows.crud` exposes read intent through `ReadWriteMode` on the context and `ExecutionType.Read` on command and transaction entry points.

## What The Public API Looks Like

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionString = "...",
    ReadWriteMode = ReadWriteMode.ReadOnly
};

var context = new DatabaseContext(config, factory);

await using var tx = await context.BeginTransactionAsync(
    IsolationProfile.SafeNonBlockingReads,
    ExecutionType.Read,
    cancellationToken);
```

There is no public `readOnly: true` transaction argument. Read intent is expressed through `ReadWriteMode` and `ExecutionType`.

## Enforcement Model

The exact session SQL varies by dialect, but the framework can enforce read intent through:

- connection-string shaping when the provider supports it
- dialect-specific session settings when the provider requires it
- transaction creation rules that reject write intent on a read-only context

The details live in the dialect and connection-lifecycle code, not in a separate read-only subsystem with its own public API.
