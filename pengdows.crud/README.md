# pengdows.crud

**pengdows.crud** is a SQL-first, strongly-typed, testable data access layer for .NET. It’s built for developers who
want **full control** over SQL, **predictable behavior** across databases, and **no ORM magic**.

> No LINQ. No tracking. No surprises.

---

## 🔍 Why pengdows.crud?

- Built by a dev who actually **writes SQL**, understands **ACID**, and doesn’t want ORMs rewriting queries behind their
  back.
- Works **across databases** using consistent, standards-compliant behavior.
- Handles **parameterization**, **enums**, **JSON**, **audit fields**, and **transactions**—out of the box.
- Offers full **dependency injection**, fine-grained **connection control**, and true **multi-tenancy**.

---

## ✅ Key Features

> Starting with v2.0, `TableGateway<TEntity, TRowID>` is the canonical CRUD gateway. The legacy `EntityHelper` alias has
> been removed.

- `TableGateway<TEntity, TRowID>`: automatic CRUD with custom SQL injection points.
- Full support for:
    - Enums
    - JSON
    - GUIDs
    - UTC timestamps
- Built-in **audit tracking** per entity and per field.
- **Safe SQL generation** with strict parameterization (`@`, `:`, or `?` depending on provider).
- Connection lifecycle modes: `Standard`, `KeepAlive`, `SingleWriter`, `SingleConnection`, `Best`.
- **Scoped transactions** via `TransactionContext`.
- Works cleanly with DI and ADO.NET—**no leaky abstractions**.

---

## 🧩 Supported Databases

Tested and tuned for:

- SQL Server / Express / LocalDB / Azure SQL
- PostgreSQL / TimescaleDB / CockroachDB / YugabyteDB / Aurora PostgreSQL
- Oracle
- MySQL / MariaDB / TiDB / Aurora MySQL
- SQLite
- Firebird
- DuckDB
- Snowflake (opt-in; cloud-only, requires credentials)

> All tested against .NET 8 with native ADO.NET providers. Must support `DbProviderFactory` and
`GetSchema("DataSourceInformation")`.

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

| Feature               | pengdows.crud | Raw ADO.NET | Dapper | EF Core | NHibernate |
|-----------------------|---------------|-------------|--------|---------|------------|
| Provider-Agnostic SQL | ✅             | ⚠️ Manual   | ⚠️     | ⚠️      | ⚠️         |
| Safe Parameterization | ✅             | ❌ Risky     | ⚠️     | ✅       | ✅          |
| Audit Field Support   | ✅ Built-in    | ❌           | ❌      | ⚠️      | ⚠️         |
| Change Tracking       | ❌ Explicit    | ❌           | ❌      | ✅       | ✅          |
| LINQ                  | ❌             | ❌           | ❌      | ✅       | ⚠️         |
| Strong Typing         | ✅             | ⚠️ Manual   | ⚠️     | ✅       | ✅          |
| Multi-tenancy         | ✅ Opt-in      | ❌           | ❌      | ⚠️      | ⚠️         |
| Async/Await Support   | ✅ Fully       | ⚠️ Provider | ✅      | ✅       | ⚠️         |
| Transaction Scoping   | ✅ Layered     | ❌           | ❌      | ✅       | ✅          |
| Testability           | ✅ Interfaces  | ❌           | ⚠️     | ⚠️      | ⚠️         |
| Migration Tooling     | ❌ By Design   | ❌           | ❌      | ✅       | ✅          |

---

## 📚 Documentation

Topics include:

- `TableGateway<TEntity, TRowID>`: CRUD entry point (EntityHelper alias removed in v2.0)
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
```

```csharp
using Microsoft.Data.SqlClient;
using pengdows.crud;

var db = new DatabaseContext("your-connection-string", SqlClientFactory.Instance);
var gateway = new TableGateway<MyEntity, long>(db);
```
