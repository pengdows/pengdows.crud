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

You can also instantiate `FakeDbConnection` or `FakeDbDataReader` directly if you need lower-level control for wrappers and utility classes.

