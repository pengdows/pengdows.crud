# Testing with `pengdows.crud.fakeDb`

`pengdows.crud.fakeDb` is a complete, in-process `DbProviderFactory` implementation — not a mock
framework bolted onto interfaces, a real ADO.NET provider (`DbConnection`/`DbCommand`/`DbDataReader`/
`DbTransaction`/`DbDataSource`) that never touches a socket or a file. It exists so that unit tests
for pengdows.crud-based code — and for code built *on top of* pengdows.crud, including entirely
unrelated ORMs layered over the same ADO.NET surface — never need a real database, a container, or
a network connection to prove correct behavior. Every dialect-specific code path in this library's
own ~7,700-test suite runs through it. It is not a toy: it emulates 15 real database products'
schema/version-detection responses, injects failures at any layer of the connection lifecycle,
captures bound parameter values at the exact moment a command executes (even when the caller
disposes the command before you'd otherwise be able to inspect it), and — via an opt-in in-memory
engine — can execute real `CREATE`/`INSERT`/`UPDATE`/`DELETE`/`SELECT` statements against tables
that only ever exist in a `Dictionary`.

## Quick start

```csharp
var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

using var sc = context.CreateSqlContainer("SELECT 1");
var result = await sc.ExecuteScalarRequiredAsync<int>();
```

`SupportedDatabase` passed to the factory constructor sets the dialect fakeDb emulates for every
connection it creates — schema responses, server-version strings, parameter-marker style,
identifier quoting, all of it. Fifteen real products are covered: `PostgreSql`, `SqlServer`,
`Oracle`, `Firebird`, `CockroachDb`, `MariaDb`, `MySql`, `Sqlite`, `DuckDB`, `YugabyteDb`, `TiDb`,
`Snowflake`, `AuroraMySql`, `AuroraPostgreSql`, `Db2`. `EmulatedProduct=X` in the connection string
is a fallback/override path (used when a connection is constructed directly, without a factory
already carrying the product) — it's a write-once property; once a connection has a non-`Unknown`
product, later attempts to change it via the connection string are ignored.

## Dialect emulation

Each of the 15 products has an embedded schema resource (`GetSchema()`/`GetSchema(string)`) that
drives everything `DataSourceInformation` reports for that dialect — parameter marker pattern,
max parameter count, named-parameter support, and so on. `GetEmulatedServerVersion()` returns a
canned per-product version string, and — more usefully — `fakeDbCommand` recognizes the *exact*
version-probe SQL text each real dialect actually sends (`SELECT @@VERSION`, `SELECT version()`,
Firebird's `RDB$GET_CONTEXT(...)`/`MON$SERVER_VERSION`, Db2's `SYSPROC.ENV_GET_INST_INFO`, DuckDB's
`PRAGMA VERSION`, etc.) and answers those automatically, so `DatabaseDetectionService`'s real
detection logic runs unmodified against a fake connection with nothing extra queued.

Beyond the built-in schemas:

- `SetMaxParameterLimit(int)` — override the reported max-parameter count, to exercise
  `TooManyParametersException`/auto-chunking against a specific dialect's real limit.
- `SetEmulatedTypeName(string)`/`TypeNameStartsWith(string)` — fake `GetType().FullName`, for code
  that string-matches a connection's type name (e.g. checking for `"Npgsql."`) without a real
  provider assembly present.
- `SchemaTable` — bypass the embedded schema entirely and hand fakeDb an arbitrary
  `DataSourceProductName`/`Version` pair that doesn't correspond to any real emulated product.

## Queuing results

Every queue below is per-connection (`fakeDbConnection`), FIFO, and consumed one entry per matching
execution unless noted otherwise:

```csharp
conn.EnqueueReaderResult(new[]
{
    new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Ada" },
    new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "Grace" },
});

conn.EnqueueScalarResult(42);
conn.EnqueueNonQueryResult(1);
```

`EnqueueReaderResult` has several overloads for shaping how the reader behaves, not just what it
returns: `+ int recordsAffected` (sets `RecordsAffected`), `+ Exception recordsAffectedException`
(throws only when `.RecordsAffected` is read, not during `Read()`), `+ (int failAfterRowCount,
Exception)` (the reader yields N rows successfully, then throws on the next `Read()`), and
`+ (int cancelAfterRowCount, CancellationTokenSource)` (yields N rows, then **cancels the real
token** passed into `ReadAsync` — proving genuine cancellation propagation through your code, not a
canned `OperationCanceledException`). `EnqueueMultiResultReader(IEnumerable<IEnumerable<Dictionary<string,object>>>)`
builds one reader whose `NextResult()` advances through each set, for compound statements like
`INSERT` followed by `SELECT LAST_INSERT_ID()`.

For scalar results keyed by the exact SQL rather than a queue:

- `SetScalarResultForCommand(commandText, value)` — answers every call with that exact command
  text, indefinitely (not consumed).
- `ScalarResolver` (`Func<string, object?>`) — total, exclusive control over every scalar response
  by command text, bypassing every other scalar mechanism. Let it throw for any command text you
  didn't anticipate — that's the intended way to catch a probe your code sends that the test didn't
  expect:

  ```csharp
  var connection = new fakeDbConnection
  {
      BlockSynchronousCommandExecution = true, // fail loudly if code falls back to sync calls
      ScalarResolver = commandText => commandText switch
      {
          "SELECT @@aurora_version" => "2.09.1",
          _ => throw new InvalidOperationException($"Unexpected probe: {commandText}")
      }
  };
  ```

- `EnqueueOutputParameterResult(Dictionary<string, object?>)` — applied to matching
  output/input-output/return-value parameters after the next execution, for stored-procedure `OUT`
  parameter simulation.
- `NextCommandLastInsertedId` — pre-populates the next created command's `LastInsertedId`
  (simulates MySqlConnector's OK-packet property, which some code reads instead of a `RETURNING`
  clause).

`fakeDbFactory.EnqueueReaderResult(rows)` (factory-level) is a convenience that creates a
connection, queues the reader on it, and pre-inserts it into `factory.Connections` so the *next*
`CreateConnection()` call returns it — useful when the code under test constructs its own
connection via the factory rather than one you already hold.

## Failure injection

Failure injection exists at three levels — command, connection, and factory — so a test can target
exactly the operation it needs to fail without disturbing everything else on the same connection.

**Connection-level** (`fakeDbConnection`): `SetFailOnOpen(shouldFail, skipFirstOpen)`,
`SetFailOnCommand(bool)`, `SetFailOnBeginTransaction(bool)` are boolean switches checked at the
matching ADO.NET entry point; `SetCustomFailureException(Exception)` overrides the default
`InvalidOperationException` they throw. `SetFailAfterOpenCount(int)` fails the `(N+1)`th `Open()`
on that one connection instance. `BreakConnection()`/`SetBroken()` simulate a hard-broken
connection (fails on open, command, and begin-transaction together, `ConnectionState.Broken`).
`SetOpenGate()` returns a `TaskCompletionSource<bool>` that `OpenAsync` awaits before completing —
for holding an "in-flight open" indefinitely and releasing it deterministically, to prove real
concurrent-open admission-control behavior rather than timing against a fixed `Delay`.

`SetCommandFailure(commandText, exception)` (exact-text match) fires for non-query, scalar, *and*
reader execution against that text — this is the one to reach for when a specific write statement
(e.g. a `ROLLBACK` your production code issues internally) needs to succeed or fail on cue:

```csharp
var factory = new fakeDbFactory(SupportedDatabase.Firebird);
const string failingSql = "INSERT INTO \"probe\" (\"id\") VALUES (1) RETURNING \"id\"";
factory.SetCommandFailure(failingSql, new InvalidOperationException("simulated provider failure"));

await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);
await using var container = context.CreateSqlContainer(failingSql);

var ex = await Record.ExceptionAsync(async () => await container.ExecuteReaderAsync(ExecutionType.Write));

Assert.NotNull(ex);
Assert.Contains(factory.CreatedConnections, c => c.ExecutedNonQueryTexts.Contains("ROLLBACK"));
```

(`SetCommandFailure` on the *factory* applies to every connection it creates; the same method on a
`fakeDbConnection` instance scopes to just that one connection and is checked first.)

Narrower still: `SetAsyncOnlyScalarFailure(text, ex)` fires only for `ExecuteScalarAsync`, not the
sync overload for the same text — proving a sync entry point never accidentally routes through
async. `SetPersistentScalarException(ex)` fires on every scalar call until cleared;
`SetScalarExecuteException(ex)` fires once then self-clears. `EnqueueTransientNonQueryFailures(params
Exception[])` queues N exceptions, one per call, then falls through to normal execution — for
proving a retry policy actually retries exactly N times. `SetTransactionCommitException`/
`SetTransactionRollbackException` apply to every transaction the connection begins.
`fakeDbCommand.BlockSynchronousExecution` (or its connection-level counterpart,
`BlockSynchronousCommandExecution`, applied to every command the connection creates) makes the
*synchronous* `ExecuteNonQuery`/`ExecuteScalar`/`ExecuteReader` entry points throw outright — the
async counterparts are unaffected — so a test can assert production code is genuinely async end to
end, not just nominally.

**Factory-level, for scenarios spanning many connections:**

```csharp
var factory = fakeDbFactory.CreateFailingFactory(
    SupportedDatabase.PostgreSql,
    ConnectionFailureMode.FailAfterCount,
    failAfterCount: 3);
```

`ConnectionFailureMode` (`None`, `FailOnOpen`, `FailOnCommand`, `FailOnTransaction`,
`FailAfterCount`, `Broken`) applies uniformly to every connection the factory subsequently creates.
`FailAfterCount` counts opens **across every connection the factory has created**, not per
instance — a genuinely different mechanism from the per-connection `SetFailAfterOpenCount`, useful
for proving a pool-level retry/backoff strategy that spans multiple physical connections.
`SetGlobalPersistentScalarException`/`SetGlobalTransactionCommitException`/
`SetGlobalTransactionRollbackException` apply to every connection created *after* the call (not
retroactively). `SetFailOnOpenForConnectionString`/`SetOpenGateForConnectionString` scope to one
exact connection string — so, for example, only a read-only-validation probe connection fails or
gates while the writer connection (a different connection string) opens normally.
`ThrowOnDataSourceDispose = true` makes every `FakeDbDataSource` the factory creates throw on
`Dispose()`, for proving that a *cleanup* failure never masks or replaces the original exception
already in flight from a construction failure.

## Introspection and assertions

The point of most of the above is to reach an assertion afterward. On `fakeDbConnection`:

- `OpenCount`/`OpenAsyncCount`/`CloseCount`/`DisposeCount` — simple call counters.
- `ExecutedNonQueryTexts`/`ExecutedReaderTexts`/`ExecutedScalarTexts` — every command text executed
  through that connection, in order, regardless of caller (`SqlContainer`, raw ADO.NET, or an
  entirely different ORM layered on top).
- `ExecutedNonQueryCommands`/`ExecutedReaderCommands` (`List<CapturedCommand>`, where
  `CapturedCommand` is `(string CommandText, IReadOnlyList<CapturedParameter> Parameters)`) —
  snapshots the **bound parameter name and value at the exact moment of execution**. This is the
  only reliable way to inspect a real bound value when the caller disposes the command before an
  awaited call returns control to you:

  ```csharp
  await using (var cmd = conn.CreateCommand())
  {
      cmd.CommandText = "SELECT \"Id\" FROM \"Customers\" WHERE \"Name\" = @name";
      var p = cmd.CreateParameter();
      p.ParameterName = "@name";
      p.Value = "Ada";
      cmd.Parameters.Add(p);
      await using var reader = await cmd.ExecuteReaderAsync();
  } // cmd is disposed here — too late to inspect cmd.Parameters directly

  var captured = Assert.Single(conn.ExecutedReaderCommands);
  var bound = Assert.Single(captured.Parameters);
  Assert.Equal("@name", bound.Name);
  Assert.Equal("Ada", bound.Value);
  ```

- `ConnectionStringHistory` — every value ever assigned to `.ConnectionString`, in order (catches a
  caller silently rebuilding a read-only variant with extra parameters).

On `fakeDbFactory`:

- `Connections` (mutable `List<fakeDbConnection>`) — a pre-seed queue: `CreateConnection()` pops
  from the front of this list before constructing a fresh connection, letting a test hand the
  factory a pre-configured connection to be returned on the *next* call.
- `CreatedConnections` (`IReadOnlyList<fakeDbConnection>`) — every connection the factory has
  actually handed out, in creation order. The standard way to answer "which physical connection ran
  this specific follow-up command" when `DbMode` might route different calls to different pooled
  instances (see the `SetCommandFailure` example above).
- `CreatedDataSources` (`IReadOnlyList<FakeDbDataSource>`) — every `FakeDbDataSource` created via
  `CreateDataSource` (reachable only when the factory advertises `SupportsNativeDataSource = true`),
  for verifying disposal of a data source your test never held a direct reference to.

`fakeDbTransaction` exposes `CommitCallCount`/`RollbackCallCount`/`DisposeCallCount`, plus
`CommitGate`/`RollbackGate`/`CommitStarted`/`RollbackStarted` (`ManualResetEventSlim`s) for pausing
a commit or rollback mid-flight to deliberately race a concurrent `Dispose` against it.

## In-memory persistence (`FakeDataStore`)

Everything above answers with canned/queued data. Setting `EnableDataPersistence = true` (on the
factory or a connection) switches to a genuine, if intentionally limited, in-memory SQL engine:
real `CREATE TABLE`, `INSERT` (single and multi-row `VALUES`, auto-assigning `Id` when omitted,
trimming trailing `ON CONFLICT`/`RETURNING`/`OUTPUT`/`ON DUPLICATE KEY` clauses), `UPDATE`/`DELETE`
with a real `WHERE` evaluator (`AND`, `IS [NOT] NULL`, `LIKE` with `%`/`_` wildcards, `IN (...)`,
comparisons), and `SELECT` (`*`, column lists with aliasing, `COUNT(...)`, literal no-`FROM`
selects). Tables persist for the lifetime of one `FakeDataStore` instance (`Clear()` resets); by
default every connection from one factory shares a single store.

**This is a hand-rolled parser that only recognizes the SQL shapes pengdows.crud itself generates —
not a real SQL engine.** A test passing against `FakeDataStore` proves your SQL was *shaped* the
way this parser expects; it does not prove a real database would accept or execute it correctly.
For that, use `testbed/` (Testcontainers-backed integration tests against real engines) — the same
discipline this library applies to its own test suite: fakeDb for fast, deterministic unit tests of
*your* logic; real containers for proof the generated SQL actually works.

## Building your own fixture layer

`fakeDbFactory`/`fakeDbConnection` are deliberately low-level primitives, not a fixture framework.
For a test suite that needs the same pre-seeded connection shape repeatedly (e.g. "a connection
that answers session-initialization, dialect-detection, and read-only-validation probes correctly,
then N ordinary operations"), write thin extension methods on top rather than repeating setup
inline — this repo's own test suite does exactly that (see
`pengdows.crud.Tests/FakeDbFactoryTestExtensions.cs`) as a pattern to copy, not a shipped API.

## Related docs

- [`docs/data-reader-mapper.md`](./data-reader-mapper.md) — hydrating fakeDb (or real) reader
  results into POCOs with no entity attributes.
- [`docs/connection/connection-modes.md`](./connection/connection-modes.md) — `DbMode` semantics
  that determine how many physical connections a given test scenario should expect fakeDb to hand
  out.
