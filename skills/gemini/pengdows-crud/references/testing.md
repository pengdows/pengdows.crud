# Testing with fakeDb

`pengdows.crud.fakeDb` provides a fake ADO.NET provider for fast unit tests without a real database.

## Test Stack

- Framework: xUnit (`[Fact]`, `[Theory]`)
- Preferred provider for unit tests: `pengdows.crud.fakeDb`
- Integration verification: `testbed/` and `pengdows.crud.IntegrationTests`

## Basic Setup

```csharp
using System.Data;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

public class BasicFakeDbTests
{
    [Fact]
    public async Task BuildAndExecute_WorksWithFakeProvider()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;", factory);

        using var sc = context.CreateSqlContainer("SELECT 1");
        var value = await sc.ExecuteScalarRequiredAsync<int>();

        Assert.Equal(1, value);
    }
}
```

Tests should target `TableGateway<TEntity, TRowID>` (not `EntityHelper`, which was the 1.0 name).

## Queueing Fake Results

Use `fakeDbConnection` queue APIs for deterministic command results:

```csharp
[Fact]
public async Task QueuedScalarAndReaderResults_AreReturnedInOrder()
{
    var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
    var connection = (fakeDbConnection)factory.CreateConnection();

    connection.EnqueueScalarResult(42);
    connection.EnqueueReaderResult(new[]
    {
        new Dictionary<string, object?> { ["id"] = 1, ["name"] = "A" },
        new Dictionary<string, object?> { ["id"] = 2, ["name"] = "B" }
    });

    factory.Connections.Add(connection);

    var context = new DatabaseContext("test", factory);

    using var scalarSc = context.CreateSqlContainer("SELECT COUNT(*) FROM users");
    var count = await scalarSc.ExecuteScalarRequiredAsync<int>();
    Assert.Equal(42, count);

    using var readerSc = context.CreateSqlContainer("SELECT id, name FROM users");
    await using var reader = await readerSc.ExecuteReaderAsync();

    Assert.True(await reader.ReadAsync());
    Assert.Equal(1, reader.GetInt32(reader.GetOrdinal("id")));
}
```

## Simulating Failures

```csharp
[Fact]
public void OpenFailure_ThrowsFromTheConstructor()
{
    // The first open happens while the context initializes, so the context itself throws
    var factory = fakeDbFactory.CreateFailingFactory(
        SupportedDatabase.Sqlite,
        ConnectionFailureMode.FailOnOpen);
    var config = new DatabaseContextConfiguration { ConnectionString = "Data Source=test", DbMode = DbMode.Standard };

    var ex = Assert.Throws<ConnectionFailedException>(() => new DatabaseContext(config, factory));
    Assert.Equal("InitConnect", ex.Phase);
}

[Fact]
public async Task CommandFailure_ThrowsOnExecute()
{
    var factory = fakeDbFactory.CreateFailingFactory(
        SupportedDatabase.Sqlite,
        ConnectionFailureMode.FailOnCommand);
    var config = new DatabaseContextConfiguration { ConnectionString = "Data Source=test", DbMode = DbMode.Standard };

    using var context = new DatabaseContext(config, factory);
    using var sc = context.CreateSqlContainer("UPDATE widgets SET name = 'x'");
    await Assert.ThrowsAsync<InvalidOperationException>(async () => await sc.ExecuteNonQueryAsync());
}
```

`ConnectionFailedException` is in `pengdows.crud.exceptions`. The failure modes on `ConnectionFailureMode` are `None` plus:

| Mode | Behavior |
|------|----------|
| `FailOnOpen` | Throws when the connection is opened |
| `FailOnCommand` | Throws when any command is executed |
| `FailOnTransaction` | Throws when a transaction is begun |
| `FailAfterCount` | Succeeds for N opens, then throws |
| `Broken` | Simulates a fully broken/unusable connection |

Additional failure controls are available on `fakeDbConnection`, including:

- `SetFailOnOpen(...)` — corresponds to `ConnectionFailureMode.FailOnOpen`
- `SetFailOnCommand(...)` — corresponds to `ConnectionFailureMode.FailOnCommand`
- `SetFailOnBeginTransaction(...)` — corresponds to `ConnectionFailureMode.FailOnTransaction`
- `SetFailAfterOpenCount(...)` — corresponds to `ConnectionFailureMode.FailAfterCount`
- `SetCustomFailureException(...)` — override the exception thrown on failure

Custom exception injection is supported — pass a specific exception instance to `SetCustomFailureException` to control exactly what is thrown. Connection tracking and disposal verification are also available for asserting correct resource cleanup.

## Emulating Any Supported Database

`new fakeDbFactory(SupportedDatabase.X)` (or the string overload, or `EmulatedProduct=...` in the connection string) makes the fake provider answer the
dialect's schema/version probes as that product, so the same test can run against the quoting, parameter markers and SQL shape of any of the 23
`SupportedDatabase` values. Full capability list (result injection, failure injection, parameter capture, `FakeDataStore`): `docs/testing-with-fakedb.md`.

## Recommended Coverage Pattern

- Use fakeDb for unit tests of SQL generation, parameterization, mapping, and failure handling.
- Use integration tests for provider-specific behavior and real transaction semantics.
- If fakeDb lacks a behavior you need, extend `pengdows.crud.fakeDb` rather than introducing new mocking layers.
