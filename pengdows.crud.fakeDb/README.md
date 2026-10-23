# pengdows.crud.fakeDb

`pengdows.crud.fakeDb` is a general-purpose fake ADO.NET provider for testing code that uses
`DbConnection`, `DbCommand`, `DbDataReader`, `DbTransaction`, and `DbProviderFactory` without a
real database. It is used by `pengdows.crud`, but it is not CRUD-specific: it is also useful for
testing repositories, micro-ORMs, EF-style provider code, retry policies, cancellation, transaction
handling, and any other data-access code built on ADO.NET.

The package ships an embedded schema file for each of the 23 `SupportedDatabase` values
(`xml/*.schema.xml`: PostgreSql, SqlServer, Oracle, Firebird, CockroachDb, MariaDb, MySql, Sqlite,
DuckDB, YugabyteDb, TiDb, Snowflake, AuroraMySql, AuroraPostgreSql, Db2, FlatFile, SingleStore,
SybaseASE, Spanner, Informix, SapHana, InterBase, Access), so tests can exercise provider-specific
metadata without installing every database.

## Usage

In the `pengdows.crud.Tests` project the fake provider is used to spin up a `DatabaseContext` without touching a real
database. The key pieces are `fakeDbFactory` and an `EmulatedProduct` value in the connection string:

```csharp
using pengdows.crud;
using pengdows.crud.fakeDb;

var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
var context = new DatabaseContext(
    "Data Source=test;EmulatedProduct=Sqlite",
    factory);
```

You can also use the fake provider without `DatabaseContext`. Create a `fakeDbConnection`
directly (its only constructor takes an optional shared `FakeDataStore`, so the connection string, with its
`EmulatedProduct`, is assigned through `ConnectionString`) and work with it using normal ADO.NET APIs:

```csharp
using pengdows.crud.fakeDb;

using var connection = new fakeDbConnection
{
    ConnectionString = "Data Source=ignored;EmulatedProduct=Sqlite"
};
await connection.OpenAsync();
using var command = connection.CreateCommand();
command.CommandText = "SELECT 1";
using var reader = await command.ExecuteReaderAsync();
```

This makes `pengdows.crud.fakeDb` handy for testing any code that relies on
`DbConnection` or `DbDataReader` without spinning up a real database.

## It honors the ADO.NET contract

fakeDb passes the provider-neutral ADO.NET conformance suite
([pengdows.AdoNetApiTest.conformance](https://github.com/pengdows/pengdows.AdoNetApiTest.conformance)), run by
`pengdows.crud.fakeDb.Conformance.Tests`, so code that works against it is not leaning on behavior no real provider
has. In particular:

- Executing or preparing a command needs an open connection, no open reader on that command, and (when one is set) that
  connection's transaction; otherwise it throws `InvalidOperationException`, as every provider does.
- A committed, rolled back or disposed transaction has no `Connection` and refuses another `Commit`/`Rollback`.
- `ExecuteNonQuery` of a SELECT returns -1, `CommandBehavior.CloseConnection` closes the connection with the reader, and
  an async execute reports cancellation and failure through its task.
- `GetSchema()` returns `MetaDataCollections`; `GetSchema("DataSourceInformation")` returns the emulated product's
  embedded schema.

With `EnableDataPersistence`, the shared `FakeDataStore` answers a SELECT that reads no table as a database would:
literals, `NULL`, bound parameters, `UNION`, `ORDER BY` a column number, comments, and one result set per statement in
a batch. Set `DataStore.StrictMode` to have text that is not SQL fail with the emulated product's syntax error (a
`DbException`) and SQL the store does not model fail with `NotSupportedException`, instead of succeeding quietly.

### Reproducing a driver's defects

Real drivers depart from that contract, and code that runs on them has to survive it. `fakeDbDriverQuirks` makes each
departure a switch; start from a driver's preset or set only the one you are testing:

```csharp
var factory = new fakeDbFactory(SupportedDatabase.PostgreSql)
{
    // Npgsql 9.0.3: a committed transaction keeps its Connection, a disposed one throws ObjectDisposedException
    // from it, a command runs under another connection's transaction, ...
    DriverQuirks = fakeDbDriverQuirks.Npgsql()
};

var connection = (fakeDbConnection)factory.CreateConnection();
connection.DriverQuirks = new fakeDbDriverQuirks { PrepareWithoutOpenConnection = true }; // just this connection
```

Presets: `SqlClient()`, `Npgsql()`, `MySqlConnector()`, `MySqlData()`, `DuckDbNet()`, `OracleManagedDataAccess()`,
`SystemDataSqlite()`, `AseClient()` and `Db2()`, each naming the driver version it reproduces. Every switch's
documentation names the drivers and versions that behave that way.

## What it can test

### Observe the command that actually ran

Every connection records created commands and executed command text. The captured-command lists
also preserve the exact parameter names and values at execution time, before a disposed
`DbCommand` can clear its parameter collection:

```csharp
var connection = new fakeDbConnection
{
    ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql"
};
connection.Open();

using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM orders WHERE customer_id = @customerId";
var parameter = command.CreateParameter();
parameter.ParameterName = "customerId";
parameter.Value = 42;
command.Parameters.Add(parameter);
using var reader = command.ExecuteReader();

var executed = connection.ExecutedReaderCommands.Single();
// executed.CommandText and executed.Parameters contain the executed values.
```

Use `ExecutedNonQueryCommands` for writes, `ExecutedReaderCommands` for readers,
`ExecutedScalarTexts` for scalar commands, `ExecutedNonQueryTexts` and `ExecutedReaderTexts`
for text-only assertions, and `ExecutedReaderBehaviors` to verify `CommandBehavior`.

### Control result sets and provider metadata

Results are queued per connection, so a test can describe exactly what the next command returns:

- `EnqueueScalarResult` and `EnqueueNonQueryResult` for scalar values and affected-row counts.
- `EnqueueReaderResult` for rows, declared columns, `RecordsAffected`, and reader failures.
- `EnqueueMultiResultReader` for multiple result sets and `NextResult()` behavior.
- `EnqueueOutputParameterResult` for output and input/output parameter values.
- `SetServerVersion` for version-gated dialect behavior.
- `SetMaxParameterLimit` for parameter-limit handling.
- `SetEmulatedTypeName` for provider/type-name detection tests.
- `fakeDbDataReader.Columns` / `fakeDbColumn` for explicit `FieldType`, provider type name,
  precision, scale, nullability, and other result metadata.

`fakeDbConnection` also exposes `NextCommandLastInsertedId` for providers whose generated-key
path reads a command-level last-inserted ID.

### Fail at the exact point a real operation fails

Failures can be injected at connection creation, opening, command creation, command execution,
transaction boundaries, or a particular point in a result stream:

- `SetFailOnOpen`, `SetFailOnCommand`, `SetFailOnBeginTransaction`, `SetBroken`, and
  `SetFailAfterOpenCount`.
- `SetNonQueryExecuteException`, scalar/reader execute failures, and
  `EnqueueTransientNonQueryFailures` for ordered transient failures consumed one per attempt.
- `EnqueueReaderResult(rows, failAfterRowCount, exception)` to fail after a known number of rows.
- `EnqueueReaderResult(rows, cancelAfterRowCount, cancellationTokenSource)` to cancel the
  caller's token during streaming rather than after the result has completed.
- `SetAsyncOnlyScalarFailure` to prove that sync and async entry points use the intended provider
  operation.
- `ResetFailureConditions` to reuse a connection after a failure scenario.

### Pause real async work deterministically

`SetOpenGate()` makes the next asynchronous open wait on a returned `TaskCompletionSource<bool>`.
`SetExecuteGate()` does the same for asynchronous non-query, scalar, or reader execution, and
`ExecuteGateEntered` completes when the command reaches that gate. These gates let tests race
shutdown, cancellation, retries, and concurrent operations without sleeps or timing assumptions.

The factory also supports `SetOpenGateForConnectionString` when the test needs to control opens
created through `fakeDbFactory` rather than a directly constructed connection.

### Exercise transaction behavior

`fakeDbTransaction` records `CommitCallCount`, `RollbackCallCount`, the async call counts, and
`DisposeCallCount`. Set `CommitException`, `RollbackException`, or `DisposeException` to verify
error translation and cleanup. `CommitGate`, `RollbackGate`, `CommitStarted`, and
`RollbackStarted` allow a test to hold a transaction operation in flight and race disposal or
concurrent callers.

### Use a small in-memory store when canned results are not enough

Pass one `FakeDataStore` to multiple connections and set `EnableDataPersistence = true` when a
test needs simple create/insert/update/delete/select behavior across connections. The store is a
small regex/prefix-based parser for the SQL shapes generated by pengdows.crud; it is not a SQL
engine and a passing store test is not proof that arbitrary SQL works on a real database.

Set `FakeDataStore.StrictMode = true` when an unrecognized statement must fail loudly instead of
being tolerated. Use `FakeDataStore.Clear()` between scenarios.

## Connection Breaking for Testing

The enhanced `fakeDbConnection` supports sophisticated connection breaking functionality for testing error scenarios and
connection failures.

### Connection Failure Modes

The `fakeDbConnection` can be configured to fail in various ways:

- **FailOnOpen**: Connection fails when `Open()` or `OpenAsync()` is called
- **FailOnCommand**: Connection fails when creating commands
- **FailOnTransaction**: Connection fails when beginning transactions (`SetFailOnBeginTransaction`)
- **FailAfterCount**: Connection works for N opens then fails (`SetFailAfterOpenCount`)
- **Broken**: Connection is permanently broken (`SetBroken`)

`ConnectionFailureMode` is the enum behind the same modes for `fakeDbFactory.CreateFailingFactory(...)`.

### Basic Failure Testing

```csharp
// Create a connection that fails on open
var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
var connection = (fakeDbConnection)factory.CreateConnection();
connection.SetFailOnOpen();

// This will throw InvalidOperationException
try { connection.Open(); }
catch (InvalidOperationException) { /* Handle connection failure */ }
```

### Custom Exceptions

```csharp
var connection = (fakeDbConnection)factory.CreateConnection();
var timeoutException = new TimeoutException("Connection timed out");
connection.SetCustomFailureException(timeoutException);
connection.SetFailOnOpen();

connection.Open(); // throws TimeoutException with custom message
```

### Fail After Count

```csharp
var connection = (fakeDbConnection)factory.CreateConnection();
connection.SetFailAfterOpenCount(2);

connection.Open(); connection.Close(); // Works (1st)
connection.Open(); connection.Close(); // Works (2nd)
connection.Open(); // Throws! (3rd attempt fails)
```

### Factory-Level Configuration

```csharp
// Create factory that produces failing connections
var factory = fakeDbFactory.CreateFailingFactory(
    SupportedDatabase.PostgreSql,
    ConnectionFailureMode.FailOnOpen,
    new TimeoutException("Custom timeout"));

var connection = factory.CreateConnection();
connection.Open(); // Throws TimeoutException
```

### Command Execution Failures

```csharp
var connection = (fakeDbConnection)factory.CreateConnection();
connection.Open();

var command = (fakeDbCommand)connection.CreateCommand();
command.SetFailOnExecute(true, new TimeoutException("Query timeout"));

command.ExecuteNonQuery(); // Throws TimeoutException
command.ExecuteScalar();   // Throws TimeoutException
command.ExecuteReader();   // Throws TimeoutException
```

### Resetting Failure Conditions

```csharp
var connection = (fakeDbConnection)factory.CreateConnection();

// Set multiple failure modes
connection.SetFailOnOpen();
connection.SetFailOnCommand();

// Reset everything back to normal
connection.ResetFailureConditions();

// Now connection works normally
connection.Open(); // Succeeds
```

### Preloading Results

`fakeDbConnection` can queue up results that will be returned the next time a
command is executed. This allows tests to simulate query responses:

```csharp
var conn = new fakeDbConnection { ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite" };
conn.EnqueueScalarResult(5);
conn.EnqueueReaderResult(new[] { new Dictionary<string, object>{{"Name", "Jane"}} });
conn.Open();
using var cmd = conn.CreateCommand();
var value = (int)cmd.ExecuteScalar(); // returns 5
using var reader = cmd.ExecuteReader();
reader.Read();
var name = reader.GetString(0); // "Jane"
```

## Support

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-yellow?logo=buy-me-a-coffee)](https://buymeacoffee.com/pengdows)
