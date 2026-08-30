# Stored Procedures

`pengdows.crud` calls stored procedures through the same `ISqlContainer` you use for ordinary SQL
— there is no separate "procedure gateway" type. The library detects which of five call syntaxes
(`ProcWrappingStyle`) the connected dialect needs and renders the correct one; you write the
procedure name and bind parameters exactly as you would for any other command.

## Two calling patterns

### 1. Automatic (the common case — no return value needed)

Put the bare procedure name in the query, add parameters normally (set `.Direction` for
OUT/INOUT parameters), execute with `CommandType.StoredProcedure`. `SqlContainer` detects this
command type and calls `WrapForStoredProc` internally before running the text:

```csharp
using var sc = context.CreateSqlContainer("GetCustomerBalance");
sc.AddParameterWithValue("customerId", DbType.Int32, 42);
var balanceParam = sc.AddParameterWithValue("balance", DbType.Decimal, 0m);
balanceParam.Direction = ParameterDirection.Output;

await sc.ExecuteNonQueryAsync(CommandType.StoredProcedure);

var balance = (decimal)balanceParam.Value!;
```

### 2. Capturing a return value (SQL Server only)

Use `WrapForCreateWithReturn()`/`WrapForUpdateWithReturn()`/`WrapForDeleteWithReturn()` — all
three are equivalent aliases for the same underlying call, pick whichever name reads best at the
call site — instead of the `CommandType.StoredProcedure` path:

```csharp
using var sc = context.CreateSqlContainer("dbo.GetNextSequenceValue");
var wrapped = sc.WrapForUpdateWithReturn();
sc.Clear();
sc.Query.Append(wrapped); // "DECLARE @__ret INT;\nEXEC @__ret = dbo.GetNextSequenceValue;\nSELECT @__ret;"
var returnValue = await sc.ExecuteScalarOrNullAsync<int>();
```

`captureReturn` only works for `ProcWrappingStyle.Exec` (SQL Server) — `WrapForStoredProc` throws
`NotSupportedException` ("Capturing return value is not supported for this provider") for every
other style. A T-SQL return code and an OUTPUT parameter are different things: use pattern 1 for
OUTPUT/INOUT parameters on any dialect, and pattern 2 only when you specifically need the SQL
Server `RETURN` statement's integer value.

## What each `ProcWrappingStyle` actually generates

`ISqlDialect.ProcWrappingStyle` selects the strategy (`pengdows.crud/strategies/proc/*.cs`).
`ExecutionType` is ignored by every style except PostgreSQL and Firebird, where read vs. write
selects genuinely different syntax.

| Style | Databases | `CreateAsync`-shaped call (`ExecutionType.Write`) | `SELECT`-shaped call (`ExecutionType.Read`) |
|---|---|---|---|
| `Call` | MySQL, MariaDB, Db2 | `CALL proc_name(arg1, arg2)` | same |
| `Exec` | SQL Server | `EXEC proc_name arg1, arg2` — **space-separated, not parenthesized**; output-capable parameters get an ` OUTPUT` suffix appended per-argument (`WrapForStoredProc`'s `BuildProcedureArguments`, only for `ParameterDirection.Output`/`InputOutput`) | same |
| `Oracle` | Oracle | `BEGIN\n\tproc_name(arg1, arg2);\nEND;` — a PL/SQL anonymous block, parentheses omitted entirely when there are no arguments | same |
| `PostgreSQL` | PostgreSQL, CockroachDB, YugabyteDB | `CALL proc_name(arg1, arg2)` (requires PostgreSQL 11+; earlier versions only support functions, use `Read` for everything) | `SELECT * FROM func_name(arg1, arg2)` |
| `ExecuteProcedure` | Firebird | `EXECUTE PROCEDURE proc_name(arg1, arg2)` | `SELECT * FROM proc_name(arg1, arg2)` — Firebird disallows empty `()`, omitted entirely when there are no arguments |
| `None` | SQLite, DuckDB | `WrapForStoredProc` throws `NotSupportedException` unconditionally — stored procedures are not supported at all on these engines | — |

`RequiresStoredProcParameterNameMatch` and `MaxOutputParameters` (cataloged in
[`capability-discovery.md`](./capability-discovery.md)) further constrain what a given dialect
accepts; check them before assuming every style supports arbitrarily many OUT parameters.

## Parameter binding across styles

- **Named-parameter dialects** (`SupportsNamedParameters == true`): arguments render as
  `dialect.MakeParameterName(param)` for each bound parameter, comma-separated, trusting the
  names you gave when calling `AddParameterWithValue` — the same naming discipline as ordinary SQL.
- **Positional-parameter dialects**: arguments render as bare `?` placeholders (one per bound
  parameter, in binding order) — parameter *names* are irrelevant to the generated call text on
  these dialects, but you still need distinct names on the `DbParameter` objects themselves for
  `AddParameterWithValue`'s own bookkeeping.
- **OUT/INOUT parameters**: set `.Direction` on the `DbParameter` before executing, exactly as you
  would for any ADO.NET command. Only the `Exec` style needs the library to add anything to the
  generated SQL text (the ` OUTPUT` suffix above) — every other style relies purely on the
  provider's own parameter-direction binding.
- **Reading a value back**: after `ExecuteNonQueryAsync(CommandType.StoredProcedure)` completes,
  read `.Value` off the same `DbParameter` reference you added the parameter with (see the
  automatic-pattern example above) — there is no separate "output bag" API.

## Worked examples, three different styles

```csharp
// MySQL / MariaDB / Db2 — Call style
using var sc = mysqlContext.CreateSqlContainer("sp_update_inventory");
sc.AddParameterWithValue("sku", DbType.String, "WIDGET-1");
sc.AddParameterWithValue("delta", DbType.Int32, -5);
await sc.ExecuteNonQueryAsync(CommandType.StoredProcedure);
// Generated: CALL sp_update_inventory(?, ?)  (positional) or (@sku, @delta) (named), per provider

// Oracle — PL/SQL anonymous block
using var sc = oracleContext.CreateSqlContainer("update_inventory");
sc.AddParameterWithValue("p_sku", DbType.String, "WIDGET-1");
sc.AddParameterWithValue("p_delta", DbType.Int32, -5);
await sc.ExecuteNonQueryAsync(CommandType.StoredProcedure);
// Generated: BEGIN
//                update_inventory(:p_sku, :p_delta);
//            END;

// PostgreSQL — function (Read) vs. procedure (Write)
using var readSc = pgContext.CreateSqlContainer("get_inventory_level");
readSc.AddParameterWithValue("sku", DbType.String, "WIDGET-1");
var level = await readSc.ExecuteScalarOrNullAsync<int>(ExecutionType.Read, CommandType.StoredProcedure);
// Generated: SELECT * FROM get_inventory_level($1)

using var writeSc = pgContext.CreateSqlContainer("update_inventory");
writeSc.AddParameterWithValue("sku", DbType.String, "WIDGET-1");
writeSc.AddParameterWithValue("delta", DbType.Int32, -5);
await writeSc.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.StoredProcedure);
// Generated: CALL update_inventory($1, $2)
```

## Limits worth knowing before you rely on this

- **No multi-result-set traversal.** `ITrackedReader` deliberately rejects `NextResult()` —
  supporting arbitrary caller-driven traversal across multiple result sets would mean holding an
  unbounded connection lease. A procedure that returns more than one result set can only have its
  first result set consumed through the normal reader/load path.
- **SQLite and DuckDB don't support stored procedures at all** (`ProcWrappingStyle.None`) — this is
  a real engine limitation, not a gap in this library; `WrapForStoredProc` throws
  `NotSupportedException` immediately rather than attempting anything.
- **Return-value capture is SQL Server-only.** Every other style throws `NotSupportedException` if
  you call `WrapForCreateWithReturn()`/etc. against it.
