# pengdows.stormgate

A tiny ADO.NET connection admission controller.

StormGate limits concurrent database connection opens to reduce
connection storms that exhaust provider pools.

## Features

- Works with any `DbProviderFactory`
- Uses provider-native `DbDataSource` when available
- Falls back to a generic wrapper when not
- Limits concurrent opens using `SemaphoreSlim`
- Releases permits when connections close or dispose

## Example

```csharp
var gate = StormGate.Create(
    MySqlConnectorFactory.Instance,
    connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromMilliseconds(750));

await using var conn = await gate.OpenAsync();
```

Use with:

* Dapper
* ADO.NET
* Hangfire
* any library accepting `DbConnection`

## What this is not

StormGate is not:

* an ORM
* a retry library
* a connection pool replacement

It limits concurrent connection opens. Nothing more.

## Relationship to pengdows.crud

StormGate is a simplified extraction of the connection governance
concept used in `pengdows.crud`.

Unlike `pengdows.crud` PoolGovernor, StormGate does not derive its
effective gate size from provider pool settings, defaults, or framework
overrides. The concurrency limit is supplied explicitly by the caller.
