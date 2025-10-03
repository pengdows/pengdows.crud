# fakeDb Integration Guide

The `pengdows.crud.fakeDb` package provides an in-memory ADO.NET provider that mimics
real database behaviors so you can exercise CRUD helpers and Dapper pipelines without
spinning up an external server. The fake provider is heavily used throughout
`pengdows.crud.Tests` and this guide summarizes the patterns the test suite relies on
so you can reuse them in your own unit tests.

> **Key idea:** every fake connection must know which real database it is pretending to
> be. Set the `SupportedDatabase` when you create a factory and include an
> `EmulatedProduct=<Dialect>` segment in the connection string so metadata and parameter
> formatting behave like the desired engine.

## Quick start

Install the package and create a provider factory that targets the dialect you need.

```bash
dotnet add package pengdows.crud.fakeDb
```

```csharp
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
var connString = "Data Source=:memory:;EmulatedProduct=PostgreSql";
```

Pass the `DbProviderFactory` (or the `IFakeDbFactory` interface) anywhere your
production code expects a provider. The connection string can point to any value—the
`EmulatedProduct` token drives the behavior.

## Supported emulated databases

`SupportedDatabase` enumerates every engine fakeDb understands. Pick the value that
matches the provider you want to mimic and ensure the connection string includes the
same `EmulatedProduct` token.

| Enum value | Connection string token | Notes |
| --- | --- | --- |
| `PostgreSql` | `EmulatedProduct=PostgreSql` | Reference implementation used across the suite; mirrors PostgreSQL 15 behavior. |
| `SqlServer` | `EmulatedProduct=SqlServer` | Exercises SQL Server specific metadata such as `DataSourceInformation`. |
| `Oracle` | `EmulatedProduct=Oracle` | Useful for validating Oracle-specific parameter casing and identifier quoting. |
| `Firebird` | `EmulatedProduct=Firebird` | Covers Firebird batch limits and transaction semantics. |
| `CockroachDb` | `EmulatedProduct=CockroachDb` | Treated as PostgreSQL-flavored with Cockroach specific quirks. |
| `MariaDb` | `EmulatedProduct=MariaDb` | Shares the MySQL dialect but allows MariaDB feature checks. |
| `MySql` | `EmulatedProduct=MySql` | Mirrors MySQL metadata and parameter formatting. |
| `Sqlite` | `EmulatedProduct=Sqlite` | Simulates SQLite's relaxed SQL grammar. |
| `DuckDB` | `EmulatedProduct=DuckDB` | Provides SQL:2016 compliant behaviors for analytics workloads. |

> `SupportedDatabase.Unknown` exists for completeness but should not be used in tests;
> fakeDb requires a concrete dialect to configure metadata correctly.

## Working with the factory and connection

`fakeDbFactory` implements both `DbProviderFactory` and `IFakeDbFactory`. The latter is
helpful when you want to configure the next connection the factory creates. The tests
in `FakeDbConnectionTests` and `fakeDb/ConnectionFailureHelperTests` show how to:

- Grab the connection instance after it is created.
- Queue reader/scalar/non-query results before invoking the system-under-test (SUT).
- Reset or change failure modes between test executions.

When you need direct control over the connection instance, wrap the factory:

```csharp
private sealed class StubFactory : DbProviderFactory
{
    public IFakeDbConnection? LastConnection { get; private set; }
    public Action<IFakeDbConnection>? ConfigureNextConnection { get; set; }

    public override DbConnection CreateConnection()
    {
        var conn = new fakeDbConnection
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=SqlServer"
        };

        LastConnection = conn;
        ConfigureNextConnection?.Invoke(conn);
        ConfigureNextConnection = null;
        return conn;
    }
}
```

The pattern mirrors `MySqlSessionSettingsTests`, which captures the connection so the
SUT can be inspected after execution.

## Preloading deterministic results

Fake connections expose FIFO queues that satisfy ADO.NET operations. Seed them before
calling the SUT:

```csharp
var conn = new fakeDbConnection
{
    ConnectionString = "Data Source=:memory:;EmulatedProduct=MySql"
};

conn.EnqueueReaderResult(new[]
{
    new Dictionary<string, object>
    {
        ["id"] = 42,
        ["name"] = "demo"
    }
});

conn.EnqueueScalarResult(5);
conn.EnqueueNonQueryResult(1);
conn.SetScalarResultForCommand("SELECT COUNT(*)", 10);
```

The recommended staging flow mirrors the assertions in
`pengdows.crud.Tests/fakeDb/FakeDbTests.cs`:

1. Queue results in the same order the SUT will request them (reader → scalar →
   non-query, etc.).
2. Execute the SUT call.
3. Assert on the returned values.
4. Inspect `RemainingReaderResults`, `RemainingScalarResults`, and
   `RemainingNonQueryResults` to ensure every staged response was consumed and no
   unexpected requests were made.

Command-specific scalar overrides configured through `SetScalarResultForCommand`
always win over the general scalar queue and are ideal when the SUT issues the same
query multiple times. Tests such as `FakeDbConnectionTests.CommandSpecificScalarTakesPriority`
demonstrate this precedence.

### Staging multi-command interactions

When the SUT executes several commands in sequence (for example, a non-query followed
by a reader and a scalar), enqueue each response in the order they will be consumed:

```csharp
var conn = new fakeDbConnection
{
    ConnectionString = "Data Source=:memory:;EmulatedProduct=SqlServer"
};

conn.EnqueueNonQueryResult(1);     // INSERT
conn.EnqueueReaderResult(new[]      // SELECT ...
{
    new Dictionary<string, object>
    {
        ["id"] = 7,
        ["status"] = "Inserted"
    }
});
conn.EnqueueScalarResult(true);    // EXISTS

RunSystemUnderTest(conn);

Assert.Empty(conn.RemainingNonQueryResults);
Assert.Empty(conn.RemainingReaderResults);
Assert.Empty(conn.RemainingScalarResults);
```

The pattern above mirrors `FakeDbConnectionTests.QueueConsumptionHappensInOrder`,
which provides both the success case and negative coverage asserting that dequeuing
without staged results throws descriptive exceptions. Incorporate similar assertions
in your tests so missing queues are caught immediately.

## Observing issued commands

Every fake connection records metadata about executed commands. `ExecutedNonQueryTexts`
collects the SQL passed to `ExecuteNonQuery`. More advanced scenarios subclass either
`fakeDbConnection` or `fakeDbCommand` to track parameters; `FakeTrackedConnection` in
the test suite demonstrates the approach when you need per-command state.

## Integrating with Dapper helpers and DI

The Dapper helpers in `pengdows.crud` expect to resolve providers from dependency
injection. Combine a stub factory with your service provider to wire everything
together:

```csharp
var factory = new StubFactory();
var sp = new ServiceCollection()
    .AddSingleton<IAuditContextProvider<int>>(new FakeAuditProvider())
    .BuildServiceProvider();

var helper = new UserConfigDapperHelper(
    new ConnectionString("Data Source=:memory:;EmulatedProduct=PostgreSql"),
    factory,
    sp);

factory.ConfigureNextConnection = conn =>
{
    conn.EnqueueReaderResult(new[]
    {
        new Dictionary<string, object>
        {
            ["id"] = 42,
            ["user_id"] = 7,
            ["prompt_configuration_uuid"] = "config-123",
            ["settings"] = "{}"
        }
    });
};

var rows = await helper.Retrieve();
```

This pattern mirrors the `FakeTrackedConnection` helper and the session tests under
`pengdows.crud.Tests/MySqlSessionSettingsTests.cs`. Because each helper call opens a
fresh connection, stash configuration on the factory so queued results apply to the
next connection that gets created.

## Simulating failures

`fakeDbFactory.CreateFailingFactory` and the failure APIs on `IFakeDbConnection` allow
you to test retry logic without real outages:

- `SetFailOnOpen`, `SetFailOnCommand`, and `SetFailOnBeginTransaction` throw as soon as
the corresponding action runs.
- `SetFailAfterOpenCount` and the shared-factory `FailAfterCount` mode coordinate
state across multiple connections.
- `BreakConnection` switches the connection to `ConnectionState.Broken` to mimic a
severed transport.
- `SetCustomFailureException` replaces the default exception type.

`fakeDb/ConnectionFailureTests.cs` and `fakeDb/ConnectionBreakingExamples.cs` provide
concrete usage scenarios, including orchestrating shared open counts and alternating
between working and failing connections.

## Metadata and dialect tuning

Metadata APIs (`GetSchema`, `GetSchema("DataSourceInformation")`) behave like the
configured database. `FakeDbConnectionTests` cover the following:

- Calling `GetSchema` without configuring `EmulatedProduct` throws.
- The emulated product can be inferred from the connection string even before the
connection is opened.
- `SetServerVersion` and `SetMaxParameterLimit` allow you to mimic provider-specific
capabilities.

Use these knobs to test code paths that rely on dialect detection or parameter limits
without connecting to a live database.

## Registering with `DbProviderFactories`

When your code discovers providers via `DbProviderFactories.GetFactory`, use
`fakeDbRegistrar` to register fake providers by name. See
`FakeDbRegistrarTests.cs` for examples that map provider invariant names to
`SupportedDatabase` values and assert discovery through the registry.

## Reference examples in the test suite

The following tests are good blueprints when you need more elaborate setups:

| Scenario | Reference |
| --- | --- |
| Basic queueing and consumption | `pengdows.crud.Tests/fakeDb/FakeDbTests.cs` |
| Dialect metadata handling | `pengdows.crud.Tests/FakeDbConnectionTests.cs` |
| Recording executed SQL | `pengdows.crud.Tests/FakeTrackedConnection.cs` |
| Simulating connection failures | `pengdows.crud.Tests/fakeDb/ConnectionFailureTests.cs` |
| Coordinating failure counts across connections | `pengdows.crud.Tests/fakeDb/ConnectionFailureHelperTests.cs` |
| Wiring fakeDb into higher-level helpers | `pengdows.crud.Tests/MySqlSessionSettingsTests.cs` |

Review those files whenever you need an end-to-end example; they are kept current with
the fake provider's capabilities and show both positive and negative test coverage.

## API stability guard rails

`pengdows.crud.fakeDb` ships with a [Public API baseline](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca101)
enforced by `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Changes to
`IFakeDbConnection`, `IFakeDbFactory`, or any other public fakeDb type require a
deliberate update to `pengdows.crud.fakeDb/PublicAPI.Shipped.txt`. The analyzer runs in
every build so accidental interface regressions fail fast during CI and local development.
