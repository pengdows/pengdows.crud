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
 - [Creating an Entity for EntityHelper](docs/creating-an-entity.md)
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

var db = new DatabaseContext("your-connection-string", SqlClientFactory.Instance);
var helper = new EntityHelper<MyEntity, long>(db);
```

For integration tests without a real database, use the `pengdows.crud.fakeDb` package:

```bash
dotnet add package pengdows.crud.fakeDb
```
