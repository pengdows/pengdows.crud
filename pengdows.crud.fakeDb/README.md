# pengdows.crud.fakeDb

`pengdows.crud.fakeDb` provides a fake ADO.NET provider that you can use to **mock low-level database calls**. It lets `pengdows.crud` execute SQL without a real database connection, which is handy for integration or unit tests. The package ships with schema files to emulate different products so tests remain provider agnostic.

## Usage

In the `pengdows.crud.Tests` project the fake provider is used to spin up a `DatabaseContext` without touching a real database. The key pieces are `FakeDbFactory` and an `EmulatedProduct` value in the connection string:

```csharp
using pengdows.crud;
using pengdows.crud.FakeDb;

var factory = new FakeDbFactory(SupportedDatabase.Sqlite.ToString());
var context = new DatabaseContext(
    "Data Source=test;EmulatedProduct=Sqlite",
    factory);
```

You can also use the fake provider without `DatabaseContext`. Create a `FakeDbConnection`
directly and work with it using normal ADO.NET APIs:

```csharp
using pengdows.crud.FakeDb;

using var connection = new FakeDbConnection("Data Source=ignored;EmulatedProduct=Sqlite");
await connection.OpenAsync();
using var command = connection.CreateCommand();
command.CommandText = "SELECT 1";
using var reader = await command.ExecuteReaderAsync();
```

This makes `pengdows.crud.fakeDb` handy for testing any code that relies on
`DbConnection` or `DbDataReader` without spinning up a real database.

## Connection Breaking for Testing

The enhanced `FakeDbConnection` supports sophisticated connection breaking functionality for testing error scenarios and connection failures.

### Connection Failure Modes

The `FakeDbConnection` can be configured to fail in various ways:

- **FailOnOpen**: Connection fails when `Open()` or `OpenAsync()` is called
- **FailOnCommand**: Connection fails when creating commands
- **FailOnTransaction**: Connection fails when beginning transactions  
- **FailAfterCount**: Connection works for N operations then fails
- **Broken**: Connection is permanently broken

### Basic Failure Testing

```csharp
// Create a connection that fails on open
var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
var connection = (FakeDbConnection)factory.CreateConnection();
connection.SetFailOnOpen();

// This will throw InvalidOperationException
try { connection.Open(); } 
catch (InvalidOperationException) { /* Handle connection failure */ }
```

### Custom Exceptions

```csharp
var connection = (FakeDbConnection)factory.CreateConnection();
var timeoutException = new TimeoutException("Connection timed out");
connection.SetCustomFailureException(timeoutException);
connection.SetFailOnOpen();

connection.Open(); // throws TimeoutException with custom message
```

### Fail After Count

```csharp
var connection = (FakeDbConnection)factory.CreateConnection();
connection.SetFailAfterOpenCount(2);

connection.Open(); connection.Close(); // Works (1st)
connection.Open(); connection.Close(); // Works (2nd) 
connection.Open(); // Throws! (3rd attempt fails)
```

### Factory-Level Configuration

```csharp
// Create factory that produces failing connections
var factory = FakeDbFactory.CreateFailingFactory(
    SupportedDatabase.PostgreSql, 
    ConnectionFailureMode.FailOnOpen,
    new TimeoutException("Custom timeout"));

var connection = factory.CreateConnection();
connection.Open(); // Throws TimeoutException
```

### Command Execution Failures

```csharp
var connection = (FakeDbConnection)factory.CreateConnection();
connection.Open();

var command = (FakeDbCommand)connection.CreateCommand();
command.SetFailOnExecute(true, new TimeoutException("Query timeout"));

command.ExecuteNonQuery(); // Throws TimeoutException
command.ExecuteScalar();   // Throws TimeoutException  
command.ExecuteReader();   // Throws TimeoutException
```

### Resetting Failure Conditions

```csharp
var connection = (FakeDbConnection)factory.CreateConnection();

// Set multiple failure modes
connection.SetFailOnOpen();
connection.SetFailOnCommand();

// Reset everything back to normal
connection.ResetFailureConditions();

// Now connection works normally
connection.Open(); // Succeeds
```

### Preloading Results

`FakeDbConnection` can queue up results that will be returned the next time a
command is executed. This allows tests to simulate query responses:

```csharp
var conn = new FakeDbConnection("Data Source=:memory:;EmulatedProduct=Sqlite");
conn.EnqueueScalarResult(5);
conn.EnqueueReaderResult(new[] { new Dictionary<string, object>{{"Name", "Jane"}} });
conn.Open();
using var cmd = conn.CreateCommand();
var value = (int)cmd.ExecuteScalar(); // returns 5
using var reader = cmd.ExecuteReader();
reader.Read();
var name = reader.GetString(0); // "Jane"
```

