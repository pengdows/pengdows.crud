# pengdows.crud
[![NuGet](https://img.shields.io/nuget/v/pengdows.crud.svg)](https://www.nuget.org/packages/pengdows.crud)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml/badge.svg)](https://github.com/pengdows/pengdows.crud/actions)
[![Coverage](https://codecov.io/gh/pengdows/pengdows.crud/branch/main/graph/badge.svg)](https://codecov.io/gh/pengdows/pengdows.crud)

**pengdows.crud** is a SQL-first, strongly-typed, testable data access layer for .NET. It’s built for developers who want **full control** over SQL, **predictable behavior** across databases, and **no ORM magic**.

> No LINQ. No tracking. No surprises.

---

## 🔍 Why pengdows.crud?

- Built by a dev who actually **writes SQL**, understands **ACID**, and doesn’t want ORMs rewriting queries behind their back.
- Works **across databases** using consistent, standards-compliant behavior.
- Handles **parameterization**, **enums**, **JSON**, **audit fields**, and **transactions**—out of the box.
- Offers full **dependency injection**, fine-grained **connection control**, and true **multi-tenancy**.

---

## ✅ Key Features

- `EntityHelper<TEntity, TRowID>`: automatic CRUD with custom SQL injection points.
- `TRowID` must be a primitive integer type, `Guid`, or `string` (nullable forms are allowed, but retrieval by ID requires a non-null value).
- Full support for:
  - Enums
  - JSON
  - GUIDs
  - UTC timestamps
- Built-in **audit tracking** per entity and per field.
- **Safe SQL generation** with strict parameterization (`@`, `:`, or `?` depending on provider).
- ANSI-compliant double-quote identifiers and named parameters across all dialects.
- Connection lifecycle modes: `New`, `Shared`, `KeepAlive`.
- **Scoped transactions** via `TransactionContext`.
- Works cleanly with DI and ADO.NET—**no leaky abstractions**.
- Automatic database version detection for feature gating.
- MERGE statements supported on SQL Server, Oracle, Firebird, and PostgreSQL 15+.

---

## 🧩 Supported Databases

Tested and tuned for:

- SQL Server / Express / LocalDB
- PostgreSQL / TimescaleDB
- Oracle
- MySQL / MariaDB
- SQLite
- Firebird
- CockroachDB

> All tested against .NET 8 with native ADO.NET providers. Must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.

---

## ❌ Not Supported

Due to missing or outdated .NET providers:

- TimesTen
- DB2
- Informix
- Sybase ASE
- SQL Anywhere

Note: DB2 and Sybase ASE are not planned and will not be implemented.

Want support? Ask the vendor to ship a **real** ADO.NET provider.

---

## 🚫 Not an ORM — On Purpose

`pengdows.crud` doesn't:
- Track entities
- Auto-generate complex queries
- Obfuscate SQL

Instead, it helps you write **real SQL** that's:
- **Predictable**
- **Testable**
- **Secure**

---

## 🧠 Philosophy

- **Primary keys ≠ pseudokeys**
- **Open late, close early** — manage connections responsibly
- **Parameterize everything** — always
- **Audit everything** — store in UTC
- **Don't assume** — use provider metadata (`DbProviderFactory`, `GetSchema`)
- **Test in production-like environments** — not theory

---

## 🔬 Tool Comparison

| Feature                     | pengdows.crud | Raw ADO.NET | Dapper | EF Core | NHibernate |
|----------------------------|---------------|-------------|--------|---------|------------|
| Provider-Agnostic SQL      | ✅            | ⚠️ Manual   | ⚠️     | ⚠️     | ⚠️         |
| Safe Parameterization      | ✅            | ❌ Risky    | ⚠️     | ✅     | ✅         |
| Audit Field Support        | ✅ Built-in   | ❌          | ❌     | ⚠️     | ⚠️         |
| Change Tracking            | ❌ Explicit   | ❌          | ❌     | ✅     | ✅         |
| LINQ                       | ❌            | ❌          | ❌     | ✅     | ⚠️         |
| Strong Typing              | ✅            | ⚠️ Manual   | ⚠️     | ✅     | ✅         |
| Multi-tenancy              | ✅ Opt-in     | ❌          | ❌     | ⚠️     | ⚠️         |
| Async/Await Support        | ✅ Fully      | ⚠️ Provider | ✅     | ✅     | ⚠️         |
| Transaction Scoping        | ✅ Layered    | ❌          | ❌     | ✅     | ✅         |
| Testability                | ✅ Interfaces | ❌          | ⚠️     | ⚠️     | ⚠️         |
| Migration Tooling          | ❌ By Design  | ❌          | ❌     | ✅     | ✅         |

---

## 📚 Documentation


Topics include:

- `EntityHelper<TEntity, TRowID>`
- `SqlContainer`
- `DbMode` and connection management
- Audit and UTC logging
- Transaction scopes
- Type coercion and mapping
- Primary vs. pseudokeys
- Extending core helpers

---

## 🛠️ Getting Started

```bash
dotnet add package pengdows.crud
dotnet add package pengdows.crud.abstractions
```

  
If you only need the core interfaces for custom implementations, reference the
`pengdows.crud.abstractions` package:

```bash
dotnet add package pengdows.crud.abstractions
```


```csharp
using System.Data.SqlClient;
using pengdows.crud;

var context = new DatabaseContext("your-connection-string", SqlClientFactory.Instance);

var sc = context.CreateSqlContainer();
sc.Query.Append("SELECT CURRENT_TIMESTAMP()");
var dt = sc.ExecuteScalar<DateTime>();
```

For integration tests without a real database, use the `pengdows.crud.fakeDb` package:

```bash
dotnet add package pengdows.crud.fakeDb
```

---

## 🧪 Running Tests Inside Docker

If you do not have the .NET SDK installed locally, you can execute the full test
suite inside a disposable Docker container using the official SDK image. The
repository ships with `tools/run-tests-in-container.sh` to streamline the
workflow:

```bash
./tools/run-tests-in-container.sh
```

The script mounts the current working directory, disables the first-time
experience prompts, and runs `dotnet test -c Release` with TRX logging. You can
override the Docker image or pass custom `dotnet` arguments:

```bash
# Use a different SDK build
./tools/run-tests-in-container.sh --image mcr.microsoft.com/dotnet/sdk:8.0.201

# Forward custom arguments to dotnet (e.g., run a single project)
./tools/run-tests-in-container.sh -- dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj -c Debug
```

> **Note:** Docker must be installed and running on your machine. The container
> is ephemeral—every invocation starts fresh, so caches (NuGet packages, build
> outputs) are isolated from your host environment.

You can also map database tables to entities using attributes and work through
`EntityHelper<TEntity, TRowID>`:

```csharp
using System.Data;
using System.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;

[Table("test_table")]
public class TestTable
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]
    [Column("name", DbType.String)]
    [EnumColumn(typeof(NameEnum))]
    public NameEnum? Name { get; set; }

    [Column("description", DbType.String)]
    public string? Description { get; set; }
}

public enum NameEnum
{
    Test,
    Test2
}

var context = new DatabaseContext("your-connection-string", SqlClientFactory.Instance);
var helper = new EntityHelper<TestTable, long>(context);

var row = new TestTable { Id = 1, Name = NameEnum.Test, Description = "demo" };

// Build an INSERT without executing yet
var insert = helper.BuildCreate(row);
await insert.ExecuteNonQueryAsync();

// Retrieve by ID
var found = await helper.RetrieveOneAsync(row.Id);

// Only override the context when running inside a TransactionContext
using var tx = context.BeginTransaction();
var sc = helper.BuildRetrieve(new[] { row.Id }, tx);
var list = await helper.LoadListAsync(sc);
tx.Commit();
```
