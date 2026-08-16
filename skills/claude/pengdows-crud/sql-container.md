# ISqlContainer & Query Building

`ISqlContainer` is the core execution and parameter binding container in `pengdows.crud`. It provides safe, zero-allocation SQL composition and direct asynchronous command execution.

---

## 1. Core Responsibilities

- **Zero-Allocation String Composition**: Uses `ISqlQueryBuilder` backed by pooled string builders.
- **Provider-Aware Quoting**: Wraps table and column identifiers per dialect rules (`WrapObjectName`).
- **Safe Parameter Binding**: Automatic parameter naming (`MakeParameterName`), type coercion, and deduplication.
- **Direct Execution**: Returns `ValueTask` across all scalar, reader, and non-query execution methods.
- **Context Cloning**: Reuses pre-built SQL structures across different transaction or tenant contexts.

---

## 2. Identifier Quoting & Parameter Formatting

Always use dialect helpers when constructing custom SQL queries:

```csharp
using var sc = context.CreateSqlContainer();

sc.Query.Append("SELECT ");
sc.Query.Append(sc.WrapObjectName("u.id"));
sc.Query.Append(", ");
sc.Query.Append(sc.WrapObjectName("u.email"));
sc.Query.Append(" FROM ");
sc.Query.Append(sc.WrapObjectName("users"));
sc.Query.Append(" ");
sc.Query.Append(sc.WrapObjectName("u"));
sc.Query.Append(" WHERE ");
sc.Query.Append(sc.WrapObjectName("u.status"));
sc.Query.Append(" = ");

var param = sc.AddParameterWithValue("status", DbType.String, "Active");
sc.Query.Append(sc.MakeParameterName(param));

await using var reader = await sc.ExecuteReaderAsync();
```

> [!TIP]
> **Roslyn Analyzer PGC008**: `pengdows.crud.analyzers` will produce a compiler error if literal unparameterized values are injected into SQL predicates or join conditions. Always use `AddParameterWithValue`.

---

## 3. Parameter Naming Convention

| Prefix | Usage | Example |
|---|---|---|
| `i{n}` | INSERT values | `i0`, `i1` |
| `s{n}` | UPDATE SET assignments | `s0`, `s1` |
| `w{n}` | WHERE IN / filter clauses | `w0`, `w1` |
| `k{n}` | WHERE key/id lookups | `k0` |
| `v{n}` | Optimistic lock version predicate | `v0` |
| `b{n}` | Batch row parameters | `b0_0`, `b0_1` |
| `j{n}` | JOIN conditions | `j0` |

---

## 4. Execution API (ValueTask)

All execution methods return `ValueTask` for minimal allocation overhead:

```csharp
// Non-Query (returns affected rows)
ValueTask<int> ExecuteNonQueryAsync(CommandType type = CommandType.Text, CancellationToken ct = default);

// Scalar (Required throws if no rows or DBNull)
ValueTask<T> ExecuteScalarRequiredAsync<T>(CommandType type = CommandType.Text, CancellationToken ct = default);

// Scalar (OrNull returns null if no rows or DBNull)
ValueTask<T?> ExecuteScalarOrNullAsync<T>(CommandType type = CommandType.Text, CancellationToken ct = default);

// Scalar (Unambiguous None vs Null vs Value)
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType type = CommandType.Text, CancellationToken ct = default);

// Tracked Reader (pins connection lease until disposed)
ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type = CommandType.Text, CancellationToken ct = default);
```

---

## 5. Cloning Containers for Reuse

A pre-built container can be cloned to update parameters or switch execution context without re-generating SQL:

```csharp
var template = gateway.BuildBaseRetrieve("o");
template.Query.Append(" WHERE ");
template.Query.Append(template.WrapObjectName("o.customer_id"));
template.Query.Append(" = ");
var p = template.AddParameterWithValue("cid", DbType.Int64, 0L);
template.Query.Append(template.MakeParameterName(p));

// Clone for another customer
var clone = template.Clone();
clone.SetParameterValue(p.ParameterName, 42L);
var orders = await gateway.LoadListAsync(clone);

// Clone into a transaction context
await using var tx = await context.BeginTransactionAsync();
var txClone = template.Clone(tx);
txClone.SetParameterValue(p.ParameterName, 99L);
var txOrders = await gateway.LoadListAsync(txClone);
```

---

## 6. Stored Procedure Normalization (`ProcWrappingStyle`)

When invoking `CommandType.StoredProcedure`, `ISqlContainer` uses the dialect's `IProcWrappingStrategy` to format the call automatically:

| Strategy | Engine | Syntax Generated |
|---|---|---|
| `ExecProcWrapping` | SQL Server | `EXEC [proc_name] @p0, @p1` |
| `CallProcWrapping` | MySQL, MariaDB, DB2, Snowflake | `CALL `proc_name`(@p0, @p1)` |
| `PostgresProcWrapping` | PostgreSQL, CockroachDB, Yugabyte | Read: `SELECT * FROM "func"(@p0)` <br> Write: `CALL "proc"(@p0)` |
| `OracleProcWrapping` | Oracle | `BEGIN "PROC"(:p0); END;` |
| `ExecuteProcedureWrapping` | Firebird | `EXECUTE PROCEDURE "PROC"(@p0)` |
| `UnsupportedProcWrapping` | SQLite, TiDB | Throws clear exception that procs are unsupported |

---

## 7. Homogenized Exception Hierarchy

All native provider errors are normalized into a unified exception model:

```
DatabaseException (IsTransient, SqlState, ErrorCode, Database)
  ├── Transient (IsTransient == true) — Safe to retry via Polly / backoff
  │     ├── DeadlockException
  │     ├── SerializationConflictException
  │     ├── CommandTimeoutException
  │     ├── ConnectionFailedException
  │     └── PoolSaturatedException
  └── Permanent (IsTransient == false) — Do not retry without changing data
        ├── UniqueConstraintViolationException
        ├── ForeignKeyViolationException
        ├── NotNullViolationException
        ├── CheckConstraintViolationException
        └── ConcurrencyConflictException
```

> [!NOTE]
> `OperationCanceledException` is **never wrapped**—it always propagates directly to support cooperative cancellation tokens.

