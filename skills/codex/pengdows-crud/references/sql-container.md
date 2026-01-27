# SqlContainer Detailed Documentation

The `SqlContainer` class in pengdows.crud wraps and simplifies direct SQL execution. It handles connections, parameters, and logging in a consistent, safe, and database-agnostic way.

## Purpose

SqlContainer is responsible for:

- Executing raw or generated SQL
- Managing parameters in a portable way
- Handling command lifecycle and cleanup
- Enforcing DbMode/read-write rules (read-only, write-only)
- Safely invoking stored procedures across supported databases

## Construction

Instances of SqlContainer are constructed internally by `DatabaseContext` or `TransactionContext`, both implement `IDatabaseContext`.

```csharp
var sc = context.CreateSqlContainer();
```

You may also optionally pass a pre-existing query string.

## Key Members

### Query

A `StringBuilder` used to build the SQL command text.

- This allows in-place construction of SQL fragments
- SQL can be inspected or logged before execution

### AddParameter / AddParameters / AddParameterWithValue

Used to bind parameters to the command.

- Automatically supports `@name`, `:name`, or positional `?`
- Parameters are created using `DbProviderFactory`
- `AddParameterWithValue` creates and adds parameter in one call

```csharp
// Method 1: Create parameter, then add it
var p = context.CreateDbParameter("email", DbType.String, email);
sc.AddParameter(p);
sc.Query.Append(context.MakeParameterName(p));

// Method 2: Create and add parameter in one call (preferred)
var param = sc.AddParameterWithValue("email", DbType.String, email);
sc.Query.Append(sc.MakeParameterName(param));
```

## Execution Methods

| Method | Returns | Purpose |
|--------|---------|---------|
| `ExecuteReaderAsync(CommandType)` | `ITrackedReader` | Runs query, returns reader (extends IDataReader) |
| `ExecuteNonQueryAsync(CommandType)` | `int` | Returns affected row count |
| `ExecuteScalarAsync<T>(CommandType)` | `T?` | Returns single coerced value |
| `ExecuteScalarWriteAsync<T>(CommandType)` | `T?` | Scalar on write connection (for INSERT...RETURNING) |

## Command Preparation

All commands are prepared with proper:

- Parameter limit enforcement
- CommandType (Text or StoredProcedure)
- Statement preparation (if supported)
- Connection open behavior (auto-opened if closed)

## Stored Procedure Support

ADO.NET supports three values for the `CommandType` enumeration:

- `CommandType.Text` – The default; executes the provided SQL string as-is
- `CommandType.StoredProcedure` – Intended to call a stored procedure
- `CommandType.TableDirect` – Used to select all rows from a table without SQL (not supported)

### Behavior in This Library

While ADO.NET allows `CommandType.StoredProcedure`, it does not automatically wrap the command in the correct syntax for the underlying database.

This library addresses that limitation:

1. It accepts `CommandType.StoredProcedure`
2. Internally rewrites the command into valid SQL using appropriate syntax for the target database
3. Sets the command back to `CommandType.Text` before executing

This ensures stored procedures work across all supported databases without requiring database-specific formatting.

### Procedure Wrapping Syntax by Database

| Database | Syntax Used |
|----------|-------------|
| SQL Server | `EXEC procName` |
| Oracle | `BEGIN procName; END;` |
| PostgreSQL | `CALL procName()` or `SELECT * FROM procName()` |
| MySQL / MariaDB | `CALL procName()` |
| Firebird | `EXECUTE PROCEDURE procName` |

### Not Supported

`CommandType.TableDirect` is not supported, as it bypasses SQL entirely and is of limited value in cross-database scenarios.

## Reader Behavior

- If in `TransactionContext` or `SingleConnection` mode, connection stays open
- Otherwise, `CommandBehavior.CloseConnection` is used to auto-close

## Logging

- SQL is logged through `ILogger` at Information level
- Parameter values are NOT logged unless the consumer does so explicitly

## Disposal and Cleanup

- `Dispose()` clears parameters and query buffer
- Finalizer calls `Dispose(false)` to ensure unmanaged cleanup
- `Cleanup()` handles connection and command cleanup based on execution mode

## WrapObjectName

Wraps table or column names using the database's quote character. This will split and reassemble a value as well.

```csharp
var name = sc.WrapObjectName("MyTable");
// Returns "MyTable" or [MyTable] or `MyTable` or similarly appropriate value

var schemaAndName = sc.WrapObjectName("dbo.mytable");
// Returns "dbo"."mytable" or [dbo].[mytable] or `dbo`.`mytable`

var aliasedColumn = sc.WrapObjectName("o.total");
// Returns "o"."total" or [o].[total] or `o`.`total`
```

**IMPORTANT:** Always use `WrapObjectName()` for all table names, column names, and aliases in custom SQL to ensure proper quoting per database dialect.

## Complete Example

```csharp
var sc = context.CreateSqlContainer();

// Build SELECT with proper identifier quoting
sc.Query.Append("SELECT * FROM ");
sc.Query.Append(sc.WrapObjectName("Users"));
sc.Query.Append(" WHERE ");
sc.Query.Append(sc.WrapObjectName("Id"));
sc.Query.Append(" = ");

// Add parameter
var param = sc.AddParameterWithValue("userId", DbType.Int32, 42);
sc.Query.Append(sc.MakeParameterName(param));

// Execute
await using var reader = await sc.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    // Process rows
}
```

## Custom Query with Multiple Conditions

```csharp
var sc = gateway.BuildBaseRetrieve("u");

// Add WHERE clause with proper quoting
sc.Query.Append(" WHERE ");
sc.Query.Append(sc.WrapObjectName("u.status"));
sc.Query.Append(" = ");
var statusParam = sc.AddParameterWithValue("status", DbType.String, "Active");
sc.Query.Append(sc.MakeParameterName(statusParam));

sc.Query.Append(" AND ");
sc.Query.Append(sc.WrapObjectName("u.created_at"));
sc.Query.Append(" >= ");
var dateParam = sc.AddParameterWithValue("since", DbType.DateTime, DateTime.UtcNow.AddDays(-30));
sc.Query.Append(sc.MakeParameterName(dateParam));

sc.Query.Append(" ORDER BY ");
sc.Query.Append(sc.WrapObjectName("u.created_at"));
sc.Query.Append(" DESC");

var users = await gateway.LoadListAsync(sc);
```

## Stored Procedure Example

```csharp
var sc = context.CreateSqlContainer();
sc.Query.Append("GetUsersByRole");

var roleParam = sc.AddParameterWithValue("role", DbType.String, "Admin");

// Execute as stored procedure - library handles syntax per database
await using var reader = await sc.ExecuteReaderAsync(CommandType.StoredProcedure);
```
