# Testing Architecture: fakeDb & testbed

Testing in `pengdows.crud` is an active evolutionary engine that turns discovered real-world engine failure modes into executable invariants.

---

## 1. `pengdows.crud.fakeDb` (Lifecycle & Failure Laboratory)

`fakeDb` is **not a mock library** — it is a complete, in-memory ADO.NET provider implementation (`fakeDbFactory`, `fakeDbConnection`, `fakeDbCommand`, `fakeDbDataReader`, `FakeDataStore`).

### Purpose:
- Deterministically verify state machines, lock transitions, cancellation races, transaction rollbacks, and connection disposal leases in milliseconds without network I/O.

### Deterministic Result Queueing

```csharp
[Fact]
public async Task QueuedReaderResults_AreHydratedAccurately()
{
    var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
    var connection = (fakeDbConnection)factory.CreateConnection();

    connection.EnqueueScalarResult(42);
    connection.EnqueueReaderResult(new[]
    {
        new Dictionary<string, object?> { ["id"] = 1L, ["name"] = "Alice" },
        new Dictionary<string, object?> { ["id"] = 2L, ["name"] = "Bob" }
    });

    factory.Connections.Add(connection);

    var context = new DatabaseContext("Data Source=test;", factory);
    using var sc = context.CreateSqlContainer("SELECT id, name FROM users");
    await using var reader = await sc.ExecuteReaderAsync();

    Assert.True(await reader.ReadAsync());
    Assert.Equal("Alice", reader.GetString(reader.GetOrdinal("name")));
}
```

### Deterministic Failure Simulation

```csharp
[Fact]
public async Task OpenFailure_SurfacesCleanly()
{
    var factory = fakeDbFactory.CreateFailingFactory(
        SupportedDatabase.SqlServer,
        ConnectionFailureMode.FailOnOpen);

    var context = new DatabaseContext("Data Source=test;", factory);
    using var sc = context.CreateSqlContainer("SELECT 1");

    await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteScalarRequiredAsync<int>());
}
```

Available Failure Hooks:
- `SetFailOnOpen(...)`
- `SetFailOnCommand(...)`
- `SetFailOnBeginTransaction(...)`
- `SetFailAfterOpenCount(...)`
- `SetCustomFailureException(...)`
- `SetCommandFailure(commandText, ex)` — exact-text match, at the connection level (checked first)
  or the factory level (`_sharedCommandFailures`, applies to every connection the factory creates);
  fires for non-query, scalar, and reader execution alike.
- `ConnectionFailureMode` (`None`/`FailOnOpen`/`FailOnCommand`/`FailOnTransaction`/`FailAfterCount`/
  `Broken`) via `fakeDbFactory.CreateFailingFactory(...)`/`SetGlobalFailureMode(...)` — `FailAfterCount`
  counts opens across *every* connection the factory has created, not per instance.
- `SetOpenGate()`/`SetOpenGateForConnectionString(...)` — a `TaskCompletionSource` `OpenAsync` awaits
  before completing, for proving real concurrent-open admission control instead of timing a `Delay`.

### Result routing by exact command text

Beyond simple FIFO queues, `SetScalarResultForCommand(text, value)` answers every call matching
that exact SQL indefinitely (not consumed), and `ScalarResolver` (`Func<string, object?>`) takes
total, exclusive control over every scalar response by command text — let it throw for anything
unanticipated to catch a probe the test didn't expect. `SetEmulatedTypeName`/`SetMaxParameterLimit`
override `GetType().FullName`/the reported parameter limit for exercising dialect code that checks
either.

### Introspection: proving what actually executed

`ExecutedNonQueryTexts`/`ExecutedReaderTexts`/`ExecutedScalarTexts` capture every command text a
connection ran; `ExecutedNonQueryCommands`/`ExecutedReaderCommands` (`List<CapturedCommand>`, each
a `CommandText` + `IReadOnlyList<CapturedParameter>`) additionally snapshot **bound parameter
name/value at the moment of execution** — the only way to inspect a real bound value once the
caller disposes the command before you can otherwise get at it. `fakeDbFactory.CreatedConnections`
lists every physical connection instance the factory handed out, for answering "which connection
ran this specific follow-up command" under `DbMode`s that don't always reuse the same instance.

### `FakeDataStore` (opt-in in-memory SQL engine)

`EnableDataPersistence = true` switches from canned/queued responses to a real (if intentionally
limited) in-memory engine executing actual `CREATE`/`INSERT`/`UPDATE`/`DELETE`/`SELECT` against
tables that only exist in a `Dictionary`. It's a hand-rolled parser recognizing only the SQL shapes
pengdows.crud itself generates — a passing test proves SQL was *shaped* correctly, not that a real
database would accept it; that's still `testbed`'s job.

See `docs/testing-with-fakedb.md` in the repo for the full capability catalog (dialect emulation
across all 15 supported products, output-parameter/multi-result-set queuing, transaction
commit/rollback call counts and gates, and worked examples).

---

## 2. `testbed/` (Multi-Engine Conformance)

The `testbed` project coordinates real engine conformance verification against 11+ real database engines in Docker Testcontainers:
- **Engines Verified**: SQL Server, PostgreSQL, MySQL, MariaDB, Oracle, Firebird, DuckDB, SQLite, CockroachDB, YugabyteDB, TiDB, Snowflake, IBM DB2.
- **Common Conformance Suite**: [`TestProvider.cs`](file:///home/alaricd/prj/pengdows/pengdows.crud/testbed/TestProvider.cs) runs identical behavioral test suites against every database provider container to verify dialect capability parity.

---

## 3. Evolutionary Defect Absorption

When multi-engine testing reveals engine-specific nuances, the framework generalizes the behavior into dialect capabilities and locks it down with unit regression tests:
- **PostgreSQL Version Parsing**: Debian gcc version banners broke standard regex parsing $\to$ generalized parser + unit tests in `PostgreSqlVersionParsingTests.cs`.
- **PostgreSQL 18 MERGE RHS Qualification**: Disallowed table-alias qualification on `excluded.*` target columns in MERGE $\to$ dialect capability `EmitsAnsiMergeSyntax` + unit tests.
- **DB2 Generated Keys**: DB2 lacks `SELECT @@IDENTITY` and requires `FINAL TABLE (INSERT ...)` $\to$ `RequiresOutputParameterForReturning` and generated key plans.

---

## 4. Release Gates & Benchmark Validation

- **Coverage Ratchet**: CI strictly enforces that test coverage cannot decrease below previous baseline (minimum 83% floor, targeting 95%).
- **Benchmark Validation Harness**: BenchmarkDotNet test runs include plan validation harnesses asserting index existence and checking `SET STATISTICS XML` / `SHOWPLAN` to ensure benchmarks measure real database access paths.

---

## 5. Ecosystem Validation & Tooling

- **`pengdows.poco.mint`**: Schema-first POCO generation available as a .NET global CLI tool (`pengdows.poco.mint.cli`) and a Dockerized Svelte WebUI (`pengdows/pengdows.poco.mint`). Consumes `IDatabaseContext`/`ISqlDialect` to generate POCOs with correct `[Table]`, `[Column]`, `[Id]`, `[PrimaryKey]`, `[Version]`, `[NonInsertable]`, and audit attributes.
- **`pengdows.hangfire`**: Universal background job storage provider proving that a single engine built on `pengdows.crud` replaces separate community storage drivers across all 15 database engines.

