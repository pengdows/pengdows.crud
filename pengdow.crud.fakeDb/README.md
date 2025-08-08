# pengdow.crud.fakeDb

`pengdow.crud.fakeDb` provides a fake ADO.NET provider that you can use to **mock low-level database calls**. It lets `pengdow.crud` execute SQL without a real database connection, which is handy for integration or unit tests. The package ships with schema files to emulate different products so tests remain provider agnostic.

## Usage

In the `pengdow.crud.Tests` project the fake provider is used to spin up a `DatabaseContext` without touching a real database. The key pieces are `FakeDbFactory` and an `EmulatedProduct` value in the connection string:

```csharp
using pengdow.crud;
using pengdow.crud.FakeDb;

var factory = new FakeDbFactory(SupportedDatabase.Sqlite.ToString());
var context = new DatabaseContext(
    "Data Source=test;EmulatedProduct=Sqlite",
    factory);
```

You can also use the fake provider without `DatabaseContext`. Create a `FakeDbConnection`
directly and work with it using normal ADO.NET APIs:

```csharp
using pengdow.crud.FakeDb;

using var connection = new FakeDbConnection("Data Source=ignored;EmulatedProduct=Sqlite");
await connection.OpenAsync();
using var command = connection.CreateCommand();
command.CommandText = "SELECT 1";
using var reader = await command.ExecuteReaderAsync();
```

This makes `pengdow.crud.fakeDb` handy for testing any code that relies on
`DbConnection` or `DbDataReader` without spinning up a real database.

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

